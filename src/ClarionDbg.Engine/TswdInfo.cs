using System.Buffers.Binary;

namespace ClarionDbg.Engine;

public sealed class TswdSymbol
{
    public string Name = "";
    public ClaType Type = new() { Kind = TypeKind.Unknown };
    public bool IsGlobal;
    public uint Rva;            // when IsGlobal
    public int FrameOffset;     // when local/param
    public bool IsParam;
}

public sealed class TswdProc
{
    public string Name = "";
    public uint EntryRva;
    public ClaType? RetType;
    public List<TswdSymbol> Locals = new();
}

public sealed class TswdModule
{
    public string Name = "";
    public int FirstLine, LastLine;
    public List<(int Line, uint Rva)> Lines = new();
    public override string ToString() => $"{Name} ({Lines.Count})";
}

/// <summary>
/// Full parser for the Clarion/TopSpeed 'TSWD' debug blob.
/// Records are addressed by a ref into the symbol stream; the tag byte is at ref+4.
///   var  (0x04): typeRef u32 @+5, nameOff u32 @+9, offset i32 @+13
///   proc (0x05): retType @+5, nameOff @+9, entryRva @+13, localCount @+25, localRefs @+29..
///   type tags @typeRef+4: 0x11 int / 0x12 uint / 0x13 float / 0x14 char (+size u32);
///       0x23 decimal / 0x24 pdecimal (+size u32, +places u8);
///       0x08 group (+size u32, +count u32, +memberRefs); 0x18 array/string descriptor.
/// </summary>
public sealed class TswdInfo
{
    public string SourceFile = "";
    public int ModuleCount;
    public List<(int Line, uint Rva)> Lines { get; } = new();
    public List<TswdModule> Modules { get; } = new();
    public List<TswdSymbol> Globals { get; } = new();
    public List<TswdProc> Procs { get; } = new();
    public List<(string Name, uint Rva)> Procedures { get; } = new();

    // flat lookup sorted by rva: (rva, moduleIndex, line)
    (uint Rva, int Mod, int Line)[] _byRva = Array.Empty<(uint, int, int)>();

    byte[] _b = Array.Empty<byte>();
    int _nameBase, _symBase;
    readonly Dictionary<int, ClaType> _typeCache = new();

    static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    static int I32(byte[] b, int o) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o));
    static ushort U16(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));

    string Name(int off)
    {
        int s = _nameBase + off;
        if (s < 0 || s >= _b.Length) return $"?{off:X}";
        int e = Array.IndexOf(_b, (byte)0, s);
        return e > s ? System.Text.Encoding.Latin1.GetString(_b, s, e - s) : "";
    }

    public static TswdInfo? Load(PeImage pe)
    {
        var blob = pe.ReadCwDebugBlob();
        if (blob == null || !blob.AsSpan(0, 4).SequenceEqual("TSWD"u8)) return null;

        var info = new TswdInfo { _b = blob };
        int[] dir = new int[12];
        for (int i = 0; i < 12; i++) dir[i] = (int)U32(blob, 8 + 4 * i);
        int srcOff = dir[1], ltOff = dir[3], ltCnt = dir[4];
        info._nameBase = dir[6];
        info._symBase = dir[8];
        int amOff = dir[11], amCnt = dir[10];
        info._symEnd = amOff;     // symbol-record stream lives between symBase and the address map
        info.ModuleCount = (int)U32(blob, 4);

        // --- line table (flat) ---
        var flat = new List<(int Line, uint Rva)>(ltCnt);
        for (int i = 0, o = ltOff; i < ltCnt && o + 6 <= blob.Length; i++, o += 6)
            flat.Add((U16(blob, o), U32(blob, o + 2)));
        info.Lines.AddRange(flat);

        // --- modules: dir[0]=name-offset array, dir[1]=name strings,
        //     dir[2]=per-module {firstLineIdx, lastLineIdx} into the flat line table ---
        int nameArr = dir[0], nameTbl = dir[1], perMod = dir[2];
        var byRva = new List<(uint, int, int)>();
        for (int m = 0; m < info.ModuleCount; m++)
        {
            int nOff = (int)U32(blob, nameArr + 4 * m);
            string mname = info.CStr(nameTbl + nOff);
            int a = (int)U32(blob, perMod + 8 * m);
            int b = (int)U32(blob, perMod + 8 * m + 4);
            var mod = new TswdModule { Name = mname, FirstLine = a, LastLine = b };
            if (!(a == 0 && b == 0))   // {0,0} sentinel = module has no debug lines
                for (int e = a; e <= b && e < flat.Count; e++)
                {
                    mod.Lines.Add(flat[e]);
                    byRva.Add((flat[e].Rva, m, flat[e].Line));
                }
            info.Modules.Add(mod);
        }
        byRva.Sort((x, y) => x.Item1.CompareTo(y.Item1));
        info._byRva = byRva.ToArray();
        // pick the app's primary module (last non-empty .clw, usually the program) for default display
        info.SourceFile = info.Modules.LastOrDefault(m => m.Lines.Count > 0)?.Name ?? "";

        // top-level symbols — isolate each so one malformed record can't kill the load
        for (int i = 0; i < amCnt; i++)
        {
            int e = amOff + i * 8;
            if (e + 8 > blob.Length) break;
            uint rva = U32(blob, e);
            int reff = (int)U32(blob, e + 4);
            if (!info.ValidRef(reff)) continue;
            byte tag = info.RB(info._symBase + reff + 4);
            try
            {
                if (tag == 0x04)
                {
                    var sym = info.ParseVar(reff);
                    sym.IsGlobal = true; sym.Rva = (uint)sym.FrameOffset; sym.FrameOffset = 0;
                    // keep only plausible global data symbols (filters misread class/local records)
                    bool cleanName = sym.Name.Length > 0 && sym.Name[0] != '?' &&
                                     sym.Name.All(ch => ch >= 32 && ch < 127);
                    if (cleanName && sym.Rva >= 0x1000 && sym.Rva < 0x4000000) info.Globals.Add(sym);
                }
                else if (tag == 0x05)
                {
                    var p = info.ParseProc(reff);
                    info.Procs.Add(p);
                    info.Procedures.Add((p.Name, p.EntryRva));
                }
            }
            catch { /* skip records we don't understand (classes/VMTs/interfaces) */ }
        }
        return info;
    }

    // ---- bounds-safe blob readers ----
    int _symEnd;
    uint RU32(int o) => (o >= 0 && o + 4 <= _b.Length) ? BinaryPrimitives.ReadUInt32LittleEndian(_b.AsSpan(o)) : 0;
    int RI32(int o) => (o >= 0 && o + 4 <= _b.Length) ? BinaryPrimitives.ReadInt32LittleEndian(_b.AsSpan(o)) : 0;
    byte RB(int o) => (o >= 0 && o < _b.Length) ? _b[o] : (byte)0;
    bool ValidRef(int reff) => reff >= 0 && _symBase + reff + 5 <= _b.Length;
    string CStr(int off)
    {
        if (off < 0 || off >= _b.Length) return "";
        int e = Array.IndexOf(_b, (byte)0, off);
        return e > off ? System.Text.Encoding.Latin1.GetString(_b, off, e - off) : "";
    }

    TswdSymbol ParseVar(int reff)
    {
        int b = _symBase + reff;     // record start; tag at b+4
        int typeRef = (int)RU32(b + 5);
        int nameOff = (int)RU32(b + 9);
        int off = RI32(b + 13);
        return new TswdSymbol { Name = Name(nameOff), FrameOffset = off, Type = ParseType(typeRef) };
    }

    TswdProc ParseProc(int reff)
    {
        int b = _symBase + reff;
        int retTypeRef = (int)RU32(b + 5);
        int nameOff = (int)RU32(b + 9);
        uint entry = RU32(b + 13);
        int localCount = (int)RU32(b + 25);
        var proc = new TswdProc { Name = Name(nameOff), EntryRva = entry };
        if (ValidRef(retTypeRef) && retTypeRef > 0) proc.RetType = ParseType(retTypeRef);
        for (int i = 0; i < localCount && i < 1024; i++)
        {
            int lref = (int)RU32(b + 29 + 4 * i);
            if (!ValidRef(lref)) break;
            byte ltag = RB(_symBase + lref + 4);
            if (ltag != 0x04 && ltag != 0x0c) continue;
            var sym = ParseVar(lref);
            proc.Locals.Add(sym);
        }
        return proc;
    }

    ClaType ParseType(int typeRef)
    {
        if (_typeCache.TryGetValue(typeRef, out var c)) return c;
        var t = new ClaType { Kind = TypeKind.Unknown };
        if (!ValidRef(typeRef)) return t;
        _typeCache[typeRef] = t;             // guard against cycles
        int o = _symBase + typeRef + 4;      // tag
        byte tag = RB(o);
        switch (tag)
        {
            case 0x11: t.Kind = TypeKind.Int; t.Size = (int)RU32(o + 1); break;
            case 0x12: t.Kind = TypeKind.UInt; t.Size = (int)RU32(o + 1); break;
            case 0x13: t.Kind = TypeKind.Float; t.Size = (int)RU32(o + 1); break;
            case 0x14: t.Kind = TypeKind.Char; t.Size = (int)RU32(o + 1); break;
            case 0x23: t.Kind = TypeKind.Decimal; t.Size = (int)RU32(o + 1); t.Places = RB(o + 5); break;
            case 0x24: t.Kind = TypeKind.PDecimal; t.Size = (int)RU32(o + 1); t.Places = RB(o + 5); break;
            case 0x08:
                t.Kind = TypeKind.Group; t.Size = (int)RU32(o + 1);
                int cnt = (int)RU32(o + 5);
                for (int i = 0; i < cnt && i < 1024; i++)
                {
                    int mref = (int)RU32(o + 9 + 4 * i);
                    if (!ValidRef(mref)) continue;
                    byte mtag = RB(_symBase + mref + 4);    // members use tag 0x0c, plain vars 0x04
                    if (mtag != 0x04 && mtag != 0x0c) continue;
                    var m = ParseVar(mref);
                    t.Members.Add(new ClaType.GroupField(m.Name, m.FrameOffset, m.Type));
                }
                break;
            case 0x18:
                {
                    byte elemTag = RB(o + 9);
                    int elemSize = (int)RU32(o + 10);
                    int length = (int)RU32(o + 23);
                    var elem = new ClaType
                    {
                        Kind = elemTag switch
                        {
                            0x11 => TypeKind.Int,
                            0x12 => TypeKind.UInt,
                            0x13 => TypeKind.Float,
                            0x14 => TypeKind.Char,
                            _ => TypeKind.Unknown
                        },
                        Size = elemSize
                    };
                    if (length is < 0 or > 0xFFFF) length = 0;
                    if (elemTag == 0x14) { t.Kind = TypeKind.String; t.Length = length; t.Size = length; }
                    else { t.Kind = TypeKind.Array; t.Element = elem; t.Length = length; t.Size = elemSize * length; }
                    break;
                }
        }
        return t;
    }

    /// <summary>Map a code RVA to its (module, line) via the largest line-entry rva ≤ rva.</summary>
    public (string Module, int Line)? Locate(uint rva)
    {
        if (_byRva.Length == 0) return null;
        int lo = 0, hi = _byRva.Length - 1, idx = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (_byRva[mid].Rva <= rva) { idx = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        if (idx < 0) return null;
        var e = _byRva[idx];
        // don't attribute an address that is far past the last line of its module
        return (Modules[e.Mod].Name, e.Line);
    }

    public int? RvaToLine(uint rva) => Locate(rva)?.Line;

    /// <summary>Resolve a breakpoint to an address: prefer the given module, else any module.</summary>
    public uint? LineToRva(int line, string? module = null)
    {
        if (module != null)
        {
            var m = Modules.FirstOrDefault(x => x.Name.Equals(module, StringComparison.OrdinalIgnoreCase));
            if (m != null) return LineInModule(m, line);
        }
        foreach (var m in Modules) { var r = LineInModule(m, line); if (r != null) return r; }
        return null;
    }

    static uint? LineInModule(TswdModule m, int line)
    {
        foreach (var (l, r) in m.Lines) if (l == line) return r;
        uint? best = null; int bestLine = int.MaxValue;
        foreach (var (l, r) in m.Lines) if (l >= line && l < bestLine) { bestLine = l; best = r; }
        return best;
    }

    /// <summary>The procedure whose code range contains the given RVA.</summary>
    public TswdProc? ProcContaining(uint rva)
    {
        TswdProc? best = null; uint bestEntry = 0;
        foreach (var p in Procs)
            if (p.EntryRva <= rva && p.EntryRva >= bestEntry) { bestEntry = p.EntryRva; best = p; }
        return best;
    }
}
