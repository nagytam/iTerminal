namespace iTerminal.Core.AI;

public sealed record AiActionApprovalRequest(string Summary, IReadOnlyList<string> Actions);

public interface IAiActionApprovalService
{
    Task<bool> ConfirmAsync(AiActionApprovalRequest request, CancellationToken cancellationToken = default);
}
