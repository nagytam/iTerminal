using iTerminal.Core.Extensions;

namespace iTerminal.Core.AI;

/// <summary>Asks the start-up to build the AI chat service on its own thread, since its client takes a while to create.</summary>
public sealed class AiWarmUpContribution : IStartupWarmUpContribution
{
    public Type ServiceType { get; } = typeof(IAiChatService);
}
