# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A modern, source-level debugger for **Clarion / TopSpeed** 32-bit executables built in Debug
mode (`vid=full`). It works by parsing the EXE's embedded **`TSWD`** debug blob and driving the
process with the standard **Win32 Debugging API** — it does **not** use Clarion's proprietary
`D32`/`Cladb` engine. The app is 32-bit (to match Clarion EXEs) but runs on 64-bit Windows.

Background reading, in order of importance:
- `FINDINGS.md` — how Clarion debug builds store debug info (PE `.cwdebug` locator + appended
  `TSWD` overlay blob), and why the Win32 Debugging API is sufficient.
- `TSWD_FORMAT.md` — the record-level spec of the TSWD blob (header/directory, line table,
  symbol/proc/type records, value encodings). This is the spec `TswdInfo.cs` implements.

## Build / run / package

No `.sln` — build the projects directly. Everything targets `net8.0-windows`, `x86`.

```bash
# Build the WPF debugger
dotnet build src/ClarionDbg.App/ClarionDbg.App.csproj -c Debug

# Run it (output assembly is named ClarionDbg.exe)
dotnet run --project src/ClarionDbg.App/ClarionDbg.App.csproj

# Run the headless TSWD parser diagnostic (no UI) against a debug EXE.
# Useful to verify parsing changes without launching the GUI.
dotnet run --project src/ClarionDbg.Probe -- <path-to-dbg.exe> <bpLine>
dotnet run --project src/ClarionDbg.Probe -- sample/dbgtest/dbgtest_dbg.exe 0          # parse-only dump
dotnet run --project src/ClarionDbg.Probe -- sample/dbgtest/dbgtest_dbg.exe 0 COMPUTE  # dump a proc's locals
dotnet run --project src/ClarionDbg.Probe -- sample/dbgtest/dbgtest_dbg.exe 0 findlocal:LocSum

# Package a release (builds self-contained single-file exe + portable zip + Inno Setup installer)
powershell installer\build-release.ps1 -Version x.y.z      # artifacts only -> installer\output\
powershell installer\publish-release.ps1 -Version x.y.z    # also creates the GitHub release (needs gh)
```

There is **no automated test suite**. Verification is done by running `ClarionDbg.Probe` against
the sample EXEs and by driving the GUI against them. The `sample/` debug EXEs (`*_dbg.exe`) are
the test fixtures: `dbgtest` (minimal), `manytypes` (type coverage), `crashtest` (break-on-crash),
`pacman`. Rebuilding a sample requires a Clarion install (see "Building a Clarion debug EXE" below).

## Architecture

Three projects, strict dependency direction `App → Engine`, `Probe → Engine` (Engine has no UI deps):

- **`ClarionDbg.Engine`** — all debug logic, no UI. The important files:
  - `PeImage.cs` — loads the PE, finds sections, RVA↔offset math, locates the `.cwdebug` blob.
  - `TswdInfo.cs` — **the TSWD parser**. Produces `Modules`, `Globals`, `Procs` (with frame-offset
    locals), the flat line table, and an RVA→(module,line) lookup. Implements `TSWD_FORMAT.md`.
    `TswdSymbol` / `TswdProc` / `TswdModule` are the parsed model.
  - `ClaType.cs` — Clarion type model (`TypeKind`) and value formatting (DECIMAL/PDECIMAL BCD,
    DATE/TIME serials, STRING/CSTRING, groups, arrays).
  - `DebugSession.cs` — **the live debugger.** Owns its own background "ClarionDbg" worker thread
    that pumps `WaitForDebugEvent`; the UI thread communicates with it via events
    (`Stopped`/`Exited`/`Log`) and an `AutoResetEvent` resume gate. Handles breakpoints
    (`0xCC` write + single-step re-arm), stepping (into/over/out via temp breakpoints + bounded
    single-step), call-stack walking, per-frame locals, thread switching, memory read/write
    (`ReadProcessMemory`/`WriteProcessMemory`), conditional/hit-count/tracepoint breakpoints,
    run-to-cursor, set-next-statement, attach, and break-on-crash. This is the largest and most
    intricate file — read it before touching stepping or breakpoint behavior.
  - `Expr.cs` — small expression evaluator for breakpoint conditions and watches
    (`count > 10`, `mylocalvar1 = 5`), resolved against a frame's locals + globals.
  - `Native.cs` — P/Invoke signatures for the Win32 Debugging API.

- **`ClarionDbg.App`** — WPF UI (one file per window). `MainWindow.xaml.cs` is the hub: it holds
  the `DebugSession`, the breakpoint master list, source view, locals/globals/watch grids, and a
  ~400ms `DispatcherTimer` that live-refreshes values from process memory while running. Secondary
  windows: `BreakpointsWindow`, `MemoryWindow` (hex), `DisassemblyWindow` (x86 via **Iced**),
  `ArrayWindow` (DIM viewer), `AttachWindow`, `EditValueWindow`. `SyntaxHighlight.cs` colorizes
  Clarion source.

- **`ClarionDbg.Probe`** — headless CLI harness over the Engine for testing/diagnosing the TSWD
  parser without the GUI. See run commands above.

### Threading model (important)
`DebugSession` runs the debuggee on its own thread because the Win32 debug loop must call
`WaitForDebugEvent`/`ContinueDebugEvent` from the same thread that created the process. All
inspection (registers, memory, stack) happens while the debuggee is stopped on that worker thread;
results are marshaled to the WPF UI thread via the `Stopped` event. Do not call debug APIs from the
UI thread.

## Conventions

- The `tools/*.py` scripts are standalone reverse-engineering aids (PE dumps, TSWD inspection) used while
  decoding the format — they are not part of the build. `tswd.py` is the original reference parser.
- Breakpoints are persisted per-EXE and restored when that EXE is reopened.
- Clarion specifics worth knowing: globals are exported `$NAME`; procedures are name-mangled
  `NAME@F<args>`; the `.cwtls` section is the fingerprint of any Clarion/TopSpeed binary; threaded
  ABC data lives in thread-local storage (handled via scope grouping in the parser/UI).

### Building a Clarion debug EXE (only if regenerating sample fixtures)
Requires a Clarion install (developed against `C:\Clarion1213999`, Clarion 12). Source must be
**CRLF**. Debug info is controlled by project property `vid=full`.
```
MSBuild.exe project.cwproj /p:Configuration=Debug /p:ClarionBinPath=C:\Clarion12\bin ^
  "/p:clarion_version=Clarion 12.0.13941" "/p:ConfigDir=%APPDATA%\SoftVelocity\Clarion\12.0"
```
`clarion_version` must match a name registered in `%APPDATA%\SoftVelocity\Clarion\12.0\ClarionProperties.xml`.

## Color / UI rules (from global user instructions)
Professional palette, **no purple**; dark theme. Use modal popups, not `alert`-style messages.
After meaningful changes, commit and push to `origin/main` and keep `README.md` current.
