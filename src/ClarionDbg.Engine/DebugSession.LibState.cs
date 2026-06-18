using System.Buffers.Binary;

namespace ClarionDbg.Engine;

// Library State: a point-in-time read of the paused thread's RTL state (ERROR/EVENT/THREAD/...).
//
// Each value is obtained by calling the matching ClaRUN.dll getter export (Cla$ERRORCODE, Cla$EVENT,
// ...) ON THE STOPPED THREAD. Because the call runs on that thread, the returned value is automatically
// that thread's per-thread state — no need to locate or decode the thread instance block.
//
// Mechanism: the same func-eval hijack used for THR$GetInstance (DebugSession.Eval.cs), but calling a
// ZERO-ARG export and reading EAX. Unlike that hijack, we must preserve the stopped thread's REAL
// registers (a getter reads the genuine thread context), so we start each call from the saved context
// and change only EIP/ESP/TF. The whole getter table runs as one batch on the worker thread; sibling
// threads are suspended once for its duration.
public sealed partial class DebugSession
{
    public enum LibKind { Signed, Unsigned, Str, Date, Time }

    /// <summary>One Library State row: a display value with the source group/name and how it was read.</summary>
    public sealed record LibStateItem(string Group, string Name, string Value, LibKind Kind, bool Ok);

    sealed record Getter(string Group, string Name, string Export, LibKind Kind);

    // The v1 getter set. All verified present + in .text of ClaRUN.dll 11.1. Deliberately excludes the
    // side-effecting exports: Cla$LONGPATH/Cla$SHORTPATH clear the error code, Cla$CLIPBOARD locks the
    // clipboard. FILEERRORCODE returns a string here (Cla$FILEERRORCODE) per the runtime's own ABI.
    static readonly Getter[] Getters =
    {
        new("Error", "ERRORCODE",     "Cla$ERRORCODE",     LibKind.Signed),
        new("Error", "ERROR",         "Cla$StackErrstr",   LibKind.Str),
        new("Error", "ERRORFILE",     "Cla$ERRORFILE",     LibKind.Str),
        new("Error", "FILEERRORCODE", "Cla$FILEERRORCODE", LibKind.Str),
        new("Error", "FILEERROR",     "Cla$FILEERRORMSG",  LibKind.Str),
        new("Event", "EVENT",         "Cla$EVENT",         LibKind.Signed),
        new("Event", "ACCEPTED",      "Cla$ACCEPTED",      LibKind.Signed),
        new("Event", "FIELD",         "Cla$FIELD",         LibKind.Signed),
        new("Event", "FOCUS",         "Cla$FOCUS",         LibKind.Signed),
        new("Event", "FIRSTFIELD",    "Cla$FIRSTFIELD",    LibKind.Signed),
        new("Event", "LASTFIELD",     "Cla$LASTFIELD",     LibKind.Signed),
        new("Event", "KEYCODE",       "Cla$KEYCODE",       LibKind.Signed),
        new("Event", "THREAD",        "Cla$THREAD",        LibKind.Signed),
        new("Other", "RUNCODE",       "Cla$RUNCODE",       LibKind.Signed),
        new("Other", "REJECTCODE",    "Cla$REJECTCODE",    LibKind.Signed),
        new("Other", "SELECTED",      "Cla$SELECTED",      LibKind.Signed),
        new("Other", "KEYCHAR",       "Cla$KEYCHAR",       LibKind.Unsigned),
        new("Other", "KEYSTATE",      "Cla$KEYSTATE",      LibKind.Unsigned),
        new("Other", "GETEXITCODE",   "Cla$GETEXITCODE",   LibKind.Signed),
        new("Other", "TODAY",         "Cla$TODAY",         LibKind.Date),
        new("Other", "CLOCK",         "Cla$CLOCK",         LibKind.Time),
    };

    // ---- request plumbing (UI thread sets, worker thread fulfils — mirrors the func-eval gate) ----
    readonly object _libGate = new();
    readonly AutoResetEvent _libDone = new(false);
    volatile List<LibStateItem>? _libItems;

    /// <summary>Read the stopped thread's Library State (RTL values) by calling the ClaRUN getter
    /// exports on it. Call from the UI thread while the debuggee is stopped. Returns the rows, or an
    /// <c>Error</c> string explaining why it can't run (never both). The values are a point-in-time
    /// snapshot — discard them on resume.</summary>
    public (IReadOnlyList<LibStateItem> Items, string? Error) ReadLibraryState()
    {
        if (_hProcess == IntPtr.Zero)
            return (Array.Empty<LibStateItem>(), "No running process.");
        if (!_canEval)
            return (Array.Empty<LibStateItem>(),
                "Library State is only available at a breakpoint/step stop. Set a breakpoint in your code and run to it.");

        // Safe-point guard: the getters assume the caller is the active, bound Clarion thread. Calling
        // them while the thread is idle INSIDE the runtime/OS (e.g. waiting in ACCEPT's message pump)
        // makes the RTL raise its internal exception (0x6BEF5E4C) and GPFs the app. So only run when the
        // stopped EIP is in the user's OWN Clarion code — a debug module that is not the runtime.
        var rt = RuntimeModule();
        if (rt == null)
            return (Array.Empty<LibStateItem>(),
                "Library State needs a DLL-runtime build (ClaRUN.dll). This EXE is locally linked — the runtime is baked in, so its getters can't be called.");

        uint eip = CurrentEip;
        var m = ModuleAt(eip);
        if (m == null || m.Info == null || m == rt)
            return (Array.Empty<LibStateItem>(),
                "Can't read here — the thread is inside the runtime or OS, not your code. Pause at a source breakpoint in one of your own procedures.");

        lock (_libGate)
        {
            _libItems = null;
            _act = Act.LibState;
            _resume.Set();                                    // wake the worker (parked in Stop)
            if (!_libDone.WaitOne(TimeSpan.FromSeconds(10)))  // safety net: 22 instant getter calls
                return (Array.Empty<LibStateItem>(), "Library State read timed out — the debug session may be unstable.");
            return (_libItems ?? new List<LibStateItem>(), null);
        }
    }

    /// <summary>Worker-thread side: suspend siblings, call every getter on the stopped thread, render
    /// the rows. Leaves the last getter's trap event un-acked (like DoFuncEval) so the process stays
    /// frozen at the original stop; the top-level loop acks it on the eventual resume.</summary>
    void DoLibState(IntPtr stoppedThread)
    {
        uint tid = _stoppedTid;
        var rows = new List<LibStateItem>();
        var suspended = new List<IntPtr>();
        try
        {
            if (!_threads.TryGetValue(tid, out var hThread)) hThread = stoppedThread;
            var saved = GetCtx(hThread);

            // suspend every OTHER thread so the nested pump only ever sees our eval thread's events
            foreach (var kv in _threads)
                if (kv.Key != tid && Native.SuspendThread(kv.Value) != 0xFFFFFFFF) suspended.Add(kv.Value);

            var rt = RuntimeModule();
            // WslDebug$NameMessage turns an event number into its EVENT:* name (see debuger.clw). 0 if the
            // runtime predates it — then EVENT shows just the number.
            uint wslRva = rt?.Pe?.FindExportRva("WslDebug$NameMessage") ?? 0;
            uint wslVa = (rt != null && wslRva != 0) ? rt.LoadBase + wslRva : 0;

            foreach (var g in Getters)
            {
                uint rva = rt?.Pe?.FindExportRva(g.Export) ?? 0;
                if (rt == null || rva == 0) { rows.Add(new(g.Group, g.Name, "<unavailable>", g.Kind, false)); continue; }
                var (ok, eax) = CallGetter(hThread, tid, saved, rt.LoadBase + rva);
                string value = ok ? Render(g.Kind, eax) : "<unavailable>";
                // Name a real, recognised event. eax==0 means no event pending; a "0x…" result is the
                // resolver's fallback for an unmapped number — both are noise, so show just the number.
                if (ok && g.Name == "EVENT" && eax != 0 && wslVa != 0
                    && ResolveEventName(hThread, tid, saved, wslVa, eax) is { Length: > 0 } nm
                    && !nm.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    value = $"{value}  ({nm})";
                rows.Add(new(g.Group, g.Name, value, g.Kind, ok));
            }
            _libItems = rows;
        }
        catch { _libItems = rows; }
        finally
        {
            foreach (var h in suspended) Native.ResumeThread(h);
            _libDone.Set();
        }
    }

    (bool ok, uint eax) CallGetter(IntPtr hThread, uint tid, Native.CONTEXT saved, uint fnVa)
        => Hijack(hThread, tid, saved, fnVa);                 // a getter is a zero-arg call (regs preserved)

    /// <summary>Call <paramref name="fnVa"/> on the stopped thread via the eval hijack. Starts from the
    /// thread's REAL registers, optionally overriding EAX/EBX for the SoftVelocity register calling
    /// convention these ClaRUN exports use (e.g. WslDebug$NameMessage: EAX=*CSTRING out, EBX=eventNum),
    /// then sets EIP, pushes a fake return, and clears TF. Continues the held event on <paramref name="tid"/>
    /// and pumps until the helper RETs into the unmapped trap (success → read EAX) or ANY OTHER exception
    /// fires on that thread (the call faulted → swallow it, never deliver to the app). Restores
    /// <paramref name="saved"/> either way and leaves the held trap/fault event un-acked. Returns (ok, eax);
    /// ok=false means it faulted. With no EAX/EBX override every register is preserved, so a register-reading
    /// getter sees genuine thread state.</summary>
    (bool ok, uint eax) Hijack(IntPtr hThread, uint tid, Native.CONTEXT saved, uint fnVa, uint? eax = null, uint? ebx = null)
    {
        var e = saved;                                        // start from the genuine registers
        e.Esp = saved.Esp - 4;
        WriteDword(e.Esp, EVAL_TRAP_VA);                      // fake return → AV we recognise on RET
        if (eax.HasValue) e.Eax = eax.Value;
        if (ebx.HasValue) e.Ebx = ebx.Value;
        e.Eip = fnVa;
        e.EFlags &= ~0x100u;                                  // ensure trap flag is clear
        Native.SetThreadContext(hThread, ref e);

        Native.ContinueDebugEvent(_pid, tid, Native.DBG_CONTINUE);   // release the held event; thread runs the call

        var buf = new byte[256];
        for (int guard = 0; guard < 100000; guard++)
        {
            if (!Native.WaitForDebugEvent(buf, 5000)) break;  // timeout safety
            uint code = U32(buf, 0), etid = U32(buf, 8);
            if (code == Native.EXCEPTION_DEBUG_EVENT && etid == tid)
            {
                uint exAddr = U32(buf, 24);
                uint retEax = GetCtx(hThread).Eax;
                Native.SetThreadContext(hThread, ref saved);  // restore; deliberately leave this event un-acked
                return (exAddr == EVAL_TRAP_VA, exAddr == EVAL_TRAP_VA ? retEax : 0u);
            }
            Native.ContinueDebugEvent(_pid, etid, Native.DBG_CONTINUE);   // unrelated event on another thread — pass through
        }
        try { Native.SetThreadContext(hThread, ref saved); } catch { }
        return (false, 0);
    }

    // Clarion EVENT codes are offset into the runtime's message-name space before lookup. C6+ (all
    // versions this debugger targets) uses 0xA000; pre-C6 used 0x1400. See debuger.clw EventOffset.
    const uint EventNameOffset = 0xA000;
    uint _scratchVa;   // a 4 KB RW page in the debuggee, allocated lazily for output-buffer eval args

    /// <summary>Resolve an event number to its EVENT:* name by calling ClaRUN's own WslDebug$NameMessage
    /// (the resolver debuger.clw uses). Verified live: 0x203→EVENT:OpenWindow, 0x201→EVENT:CloseWindow,
    /// 0x01→EVENT:Accepted. Returns "" if the scratch buffer can't be allocated or the call faults
    /// (handled gracefully, never crashes the debuggee).</summary>
    string ResolveEventName(IntPtr hThread, uint tid, Native.CONTEXT saved, uint wslVa, uint eventNum)
    {
        uint buf = ScratchBuffer();
        if (buf == 0) return "";
        WriteDword(buf, 0);   // clear first dword so an unwritten buffer reads as empty
        // SoftVelocity register convention: EAX = output *CSTRING buffer, EBX = event number (offset
        // into the runtime's message-name space). The name is written into the buffer (and returned in EAX).
        var (ok, _) = Hijack(hThread, tid, saved, wslVa, eax: buf, ebx: eventNum + EventNameOffset);
        return ok ? ReadAsciiZRemote(buf, 128) : "";
    }

    /// <summary>A reusable scratch page in the debuggee for eval calls that write to a buffer. Allocated
    /// once per session (one page; not freed — negligible, and the process is the debuggee's). 0 on failure.</summary>
    uint ScratchBuffer()
    {
        if (_scratchVa != 0) return _scratchVa;
        var p = Native.VirtualAllocEx(_hProcess, IntPtr.Zero, 4096, 0x1000 | 0x2000, 0x04);   // COMMIT|RESERVE, RW
        return _scratchVa = (uint)p.ToInt64();
    }

    string Render(LibKind kind, uint eax) => kind switch
    {
        LibKind.Str      => ReadAsciiZRemote(eax, 512),
        LibKind.Unsigned => eax.ToString(),
        _                => ((int)eax).ToString(),           // Signed / Date / Time — raw serial (UI formats)
    };

    /// <summary>Read a NUL-terminated ASCII string from the target at <paramref name="va"/> (capped),
    /// or "" when the pointer is null. Used for the string-returning getters (EAX = char*).</summary>
    string ReadAsciiZRemote(uint va, int cap)
    {
        if (va == 0) return "";
        try
        {
            var b = ReadMemory(va, cap);
            int n = Array.IndexOf(b, (byte)0);
            return System.Text.Encoding.ASCII.GetString(b, 0, n < 0 ? b.Length : n);
        }
        catch { return ""; }
    }
}
