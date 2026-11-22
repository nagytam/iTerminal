namespace iTerminal.Core.Extensions;

/// <summary>
/// The bar a feature outside iTerminal.Core adds under the content, with the height it is configured to take. The content
/// asks for it once, when it is built; when no contribution is registered there is no bar.
/// </summary>
public interface IBottomBarContribution
{
    IBottomBar? Bar { get; }

    /// <summary>The configured height in rows; <see cref="IBottomBar.PreferredHeight"/> may shrink it.</summary>
    int BarHeight { get; }
}
