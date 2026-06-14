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
    public List<(int Line, uint Rva)> Lines { get; } = new();
    public List<TswdSymbol> Globals { get; } = new();
    public List<TswdProc> Procs { get; } = new();
    public List<(string Name, uint Rva)> Procedures { get; } = new();

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

        // source file
        int se = Array.IndexOf(blob, (byte)0, srcOff);
        info.SourceFile = System.Text.Encoding.Latin1.GetString(blob, srcOff, se - srcOff);

        // line table
        for (int i = 0, o = ltOff; i < ltCnt; i++, o += 6)
            info.Lines.Add((U16(blob, o), U32(blob, o + 2)));

        // top-level symbols
        for (int i = 0; i < amCnt; i++)
        {
            uint rva = U32(blob, amOff + i * 8);
            int reff = (int)U32(blob, amOff + i * 8 + 4);
            int b = info._symBase + reff;
            if (b + 5 > blob.Length) continue;
            byte tag = blob[b + 4];
            if (tag == 0x04)
            {
                var sym = info.ParseVar(reff);
                sym.IsGlobal = true; sym.Rva = (uint)sym.FrameOffset; sym.FrameOffset = 0;
                info.Globals.Add(sym);
            }
            else if (tag == 0x05)
            {
                var p = info.ParseProc(reff);
                info.Procs.Add(p);
                info.Procedures.Add((p.Name, p.EntryRva));
            }
        }
        return info;
    }

    TswdSymbol ParseVar(int reff)
    {
        int b = _symBase + reff;     // record start; tag at b+4
        int typeRef = (int)U32(_b, b + 5);
        int nameOff = (int)U32(_b, b + 9);
        int off = I32(_b, b + 13);
        return new TswdSymbol { Name = Name(nameOff), FrameOffset = off, Type = ParseType(typeRef) };
    }

    TswdProc ParseProc(int reff)
    {
        int b = _symBase + reff;
        int retTypeRef = (int)U32(_b, b + 5);
        int nameOff = (int)U32(_b, b + 9);
        uint entry = U32(_b, b + 13);
        int localCount = (int)U32(_b, b + 25);
        var proc = new TswdProc { Name = Name(nameOff), EntryRva = entry };
        if (retTypeRef > 0 && _symBase + retTypeRef + 5 <= _b.Length) proc.RetType = ParseType(retTypeRef);
        for (int i = 0; i < localCount && i < 256; i++)
        {
            int lref = (int)U32(_b, b + 29 + 4 * i);
            if (_symBase + lref + 13 > _b.Length) break;
            if (_b[_symBase + lref + 4] != 0x04) continue;
            var sym = ParseVar(lref);
            sym.IsParam = sym.FrameOffset > 0;   // params sit above the frame base
            proc.Locals.Add(sym);
        }
        return proc;
    }

    ClaType ParseType(int typeRef)
    {
        if (_typeCache.TryGetValue(typeRef, out var c)) return c;
        var t = new ClaType { Kind = TypeKind.Unknown };
        _typeCache[typeRef] = t;             // guard against cycles
        int o = _symBase + typeRef + 4;      // tag
        if (o < 0 || o + 5 > _b.Length) return t;
        byte tag = _b[o];
        switch (tag)
        {
            case 0x11: t.Kind = TypeKind.Int; t.Size = (int)U32(_b, o + 1); break;
            case 0x12: t.Kind = TypeKind.UInt; t.Size = (int)U32(_b, o + 1); break;
            case 0x13: t.Kind = TypeKind.Float; t.Size = (int)U32(_b, o + 1); break;
            case 0x14: t.Kind = TypeKind.Char; t.Size = (int)U32(_b, o + 1); break;
            case 0x23: t.Kind = TypeKind.Decimal; t.Size = (int)U32(_b, o + 1); t.Places = _b[o + 5]; break;
            case 0x24: t.Kind = TypeKind.PDecimal; t.Size = (int)U32(_b, o + 1); t.Places = _b[o + 5]; break;
            case 0x08:
                t.Kind = TypeKind.Group; t.Size = (int)U32(_b, o + 1);
                int cnt = (int)U32(_b, o + 5);
                for (int i = 0; i < cnt && i < 256; i++)
                {
                    int mref = (int)U32(_b, o + 9 + 4 * i);
                    if (_symBase + mref + 17 > _b.Length) continue;
                    byte mtag = _b[_symBase + mref + 4];   // members use tag 0x0c, plain vars 0x04
                    if (mtag != 0x04 && mtag != 0x0c) continue;
                    var m = ParseVar(mref);
                    t.Members.Add(new ClaType.GroupField(m.Name, m.FrameOffset, m.Type));
                }
                break;
            case 0x18:
                {
                    byte elemTag = _b[o + 9];
                    int elemSize = (int)U32(_b, o + 10);
                    int length = (o + 23 + 4 <= _b.Length) ? (int)U32(_b, o + 23) : 0;
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
                    if (elemTag == 0x14) { t.Kind = TypeKind.String; t.Length = length; t.Size = length; }
                    else { t.Kind = TypeKind.Array; t.Element = elem; t.Length = length; t.Size = elemSize * length; }
                    break;
                }
        }
        return t;
    }

    public int? RvaToLine(uint rva)
    {
        int? best = null; uint bestRva = 0;
        foreach (var (line, r) in Lines)
            if (r <= rva && r >= bestRva) { bestRva = r; best = line; }
        return best;
    }

    public uint? LineToRva(int line)
    {
        foreach (var (l, r) in Lines) if (l == line) return r;
        uint? best = null; int bestLine = int.MaxValue;
        foreach (var (l, r) in Lines) if (l >= line && l < bestLine) { bestLine = l; best = r; }
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
