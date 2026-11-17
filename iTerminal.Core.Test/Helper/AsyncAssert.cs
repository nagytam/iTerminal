namespace iTerminal.Core.Test.Helper;

/// <summary>Assert helpers for async code: this MSTest version has no ThrowsExceptionAsync.</summary>
internal static class AsyncAssert
{
    /// <summary>Runs <paramref name="action"/> and returns the exception of type <typeparamref name="T"/> it throws; fails when none is thrown.</summary>
    public static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T exception)
        {
            return exception;
        }

        Assert.Fail($"Expected {typeof(T).Name}, but nothing was thrown.");
        return null!;
    }
}
