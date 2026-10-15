using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Test.Helper;

/// <summary>Assertions on asynchronous code, for the MSTest version in use, which has no awaitable exception assertion.</summary>
public static class AssertAsync
{
    /// <summary>Runs <paramref name="action"/> and returns the exception of type <typeparamref name="TException"/> it throws; fails when it throws none.</summary>
    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

        Assert.Fail($"Expected {typeof(TException).Name}, but nothing was thrown.");
        throw new InvalidOperationException("unreachable");
    }
}
