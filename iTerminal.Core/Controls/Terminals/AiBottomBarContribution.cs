using iTerminal.Core.AI;
using iTerminal.Core.Extensions;
using Microsoft.Extensions.Options;

namespace iTerminal.Core.Controls.Terminals;

/// <summary>
/// The AI prompt bar, offered to the content as its bottom bar. Registered as a singleton beside the content, which is
/// also a singleton, so the one bar it holds is the bar the content lays out.
/// </summary>
public sealed class AiBottomBarContribution(AiPromptBar bar, IOptions<AiOptions> options) : IBottomBarContribution
{
    public IBottomBar? Bar { get; } = bar;

    public int BarHeight { get; } = options.Value.BarHeight;
}
