Clarion Debugger 1.3.0

What's new since 1.2.0:

- Source resolution engine (new Clarion.SourceResolution library) — finds your .clw/.inc sources
  the way the Clarion IDE does:
  - parses redirection (.red) files, with macro/section handling;
  - reads .sln / .cwproj solutions and project file lists;
  - detects installed Clarion versions and IDE preferences;
  - indexes file lists for fast lookup.
- Link Solution window — associate a debugged EXE with its Clarion solution so the debugger
  resolves the exact project sources (remembered per solution).
- Backed by a full unit-test suite for the resolution logic (redirection, solution parsing,
  install detection, file-list indexing, association store).

Download:
- ClarionDebuggerSetup-1.3.0.exe - installer (per-user, no admin)
- ClarionDebugger-1.3.0-portable-win-x86.zip - portable single .exe (unzip & run)

Self-contained (.NET 8 runtime bundled - nothing to install). 32-bit, runs on 64-bit Windows.
Licensed under MIT. See the README for the full feature list.
