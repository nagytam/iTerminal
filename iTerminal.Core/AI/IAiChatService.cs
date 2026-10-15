namespace iTerminal.Core.AI;

public interface IAiChatService
{
    Task CheckAvailabilityAsync(AiChatModel model);
    Task SendMessageAsync(AiChatModel model, CancellationToken ct = default);
}
