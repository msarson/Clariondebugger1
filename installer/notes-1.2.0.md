Clarion Debugger 1.2.0

What's new since 1.1.0:

- Multi-DLL debugging — debug across the EXE and its loaded Clarion debug DLLs (one source/symbol catalog spanning all images).
- Pause / break into a running program (F6) — interrupt a freely-running debuggee to inspect the stack and variables.
- Hover data tips in the source view:
  - variable values (name, type, value; DATE/TIME formatted), live while running or stopped;
  - EQUATE constants (hex/binary/octal decoded to decimal), resolved from source and INCLUDE files;
  - FILE / GROUP / QUEUE record fields — e.g. STU:LastName, MAJ:Number, vGroup.gA, and browse-queue fields via BRW1.Q.STU:LastName.
- Identify thread by window — hover the running program's windows to see (and select) which thread owns each one.
- Richer Clarion syntax colouring in the source view.
- Foundation for threaded (thread-local) data evaluation; PE import parsing.

Download:
- ClarionDebuggerSetup-1.2.0.exe - installer (per-user, no admin)
- ClarionDebugger-1.2.0-portable-win-x86.zip - portable single .exe (unzip & run)

Self-contained (.NET 8 runtime bundled - nothing to install). 32-bit, runs on 64-bit Windows.
Licensed under MIT. See the README for the full feature list.
