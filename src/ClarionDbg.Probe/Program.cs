using ClarionDbg.Engine;

string exe = args.Length > 0 ? args[0] : @"C:\ai\debuger\sample\dbgtest\dbgtest_dbg.exe";
int bpLine = args.Length > 1 ? int.Parse(args[1]) : 21;

var pe = new PeImage(exe);
var info = TswdInfo.Load(pe) ?? throw new Exception("not a debug build");

Console.WriteLine($"source   : {info.SourceFile}");
Console.WriteLine($"globals  : {string.Join(", ", info.Globals.OrderBy(g => g.Rva).Select(g => $"{g.Name}@0x{g.Rva:X}"))}");
Console.WriteLine($"procs    : {string.Join(", ", info.Procedures.OrderBy(p => p.Rva).Select(p => $"{p.Name}@0x{p.Rva:X}"))}");
Console.WriteLine($"break    : line {bpLine} -> rva 0x{info.LineToRva(bpLine):X}");
Console.WriteLine(new string('-', 60));

var done = new ManualResetEventSlim();
var sess = new DebugSession(exe, pe, info);
sess.Log += s => Console.WriteLine("[engine] " + s);
sess.Exited += c => { Console.WriteLine($"[exit] code {c}"); done.Set(); };
sess.Stopped += info2 =>
{
    Console.WriteLine($"\n*** STOPPED: {info2.Reason} at EIP 0x{info2.Eip:X8} (line {info2.Line}) ***");
    Console.WriteLine("call stack:");
    foreach (var f in info2.Stack) Console.WriteLine($"   {f.Proc,-20} 0x{f.Addr:X8} {(f.Line is int l ? "line " + l : "")}");
    Console.WriteLine("locals (current procedure):");
    foreach (var v in info2.Locals)
        Console.WriteLine($"   {v.Name,-18} {v.TypeName,-14} = {v.Display}");
    Console.WriteLine("globals (typed, live values):");
    foreach (var v in info2.Globals)
        Console.WriteLine($"   {v.Name,-12} {v.TypeName,-14} = {v.Display}");
    Console.WriteLine("\n(continuing…)");
    sess.Continue();
};

sess.Start(new[] { bpLine });
done.Wait(8000);
Console.WriteLine("probe finished.");
