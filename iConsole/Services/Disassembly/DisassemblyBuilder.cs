namespace iConsole.Services.Disassembly;

/// <summary>Chooses between the IL and the native disassembler for a file's bytes and returns the lines to show.</summary>
internal static class DisassemblyBuilder
{
    /// <summary>
    /// Builds the disassembly section. Never loads or runs the file, and never throws on malformed input; only a cancelled
    /// token surfaces as <see cref="OperationCanceledException"/>.
    /// </summary>
    public static IReadOnlyList<string> Build(byte[] bytes, CancellationToken token, TimeSpan? maxDuration = null)
    {
        DisassemblyBudget budget = new(token, maxDuration);
        try
        {
            if (!ManagedIlDisassembler.TryDisassemble(bytes, budget))
            {
                NativeDisassembler.Disassemble(bytes, budget);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The input is untrusted; whatever a parser trips over must end as a note, not as a crashed viewer.
            budget.Lines.Add($"  Disassembly stopped: {ex.Message}");
        }

        return budget.Lines;
    }
}
