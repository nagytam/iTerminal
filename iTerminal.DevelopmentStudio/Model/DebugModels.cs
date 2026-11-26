namespace iTerminal.DevelopmentStudio.Model;

/// <summary>Why the debuggee stopped, as the adapter reports it (<c>breakpoint</c>, <c>step</c>, <c>exception</c>, <c>pause</c>, ...).</summary>
/// <param name="ThreadId">The thread that stopped.</param>
/// <param name="Reason">The adapter's reason text.</param>
public sealed record DebugStop(int ThreadId, string Reason);

/// <summary>One frame of the call stack. <see cref="Path"/> is empty for frames without source (native or generated code).</summary>
public sealed record DebugStackFrame(int Id, string Name, string Path, int Line);

/// <summary>A variable or watch value. <see cref="Reference"/> is non-zero when the value has children to expand.</summary>
public sealed record DebugVariable(string Name, string Value, string Type, int Reference);

/// <summary>A breakpoint the adapter has confirmed (or rejected) at a line.</summary>
public sealed record DebugBreakpointState(string Path, int Line, bool Verified);

/// <summary>A named scope of a frame (Locals, Globals, ...) and the reference its variables are read by.</summary>
public sealed record DebugScope(string Name, int Reference);
