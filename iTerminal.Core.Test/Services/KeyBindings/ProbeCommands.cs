using iConsole.Commands;

namespace iTerminal.Core.Test.Services.KeyBindings.KeyBindingsFirst
{
    public sealed class ProbeCommand : Command
    {
        public override Task ExecuteAsync(object? args) => Task.CompletedTask;
    }
}

namespace iTerminal.Core.Test.Services.KeyBindings.KeyBindingsSecond
{
    public sealed class ProbeCommand : Command
    {
        public override Task ExecuteAsync(object? args) => Task.CompletedTask;
    }
}
