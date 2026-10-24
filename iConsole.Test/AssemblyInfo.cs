using Microsoft.VisualStudio.TestTools.UnitTesting;

// Method-level parallelism with one worker per processor (Workers = 0). Classes that share process-wide
// state opt out with [DoNotParallelize] on the class.
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]
