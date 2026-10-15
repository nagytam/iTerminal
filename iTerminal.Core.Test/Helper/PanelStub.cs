using iConsole.Data;
using iConsole.Interfaces;
using Moq;

namespace iTerminal.Core.Test.Helper;

/// <summary>
/// A panel content model that says when it was re-read.
///
/// The refresh a finished file operation triggers lands on a thread-pool thread, so a test has to
/// wait for it. It waits on these signals rather than polling <see cref="Mock{T}.Verify(System.Linq.Expressions.Expression{Action{T}})"/>
/// in a loop: verifying a mock while another thread is calling into it races Moq's own invocation
/// list, which turns "the refresh has not landed yet" into an exception from inside Verify.
/// </summary>
public sealed class PanelStub
{
    private readonly ManualResetEventSlim _refreshed = new(false);
    private readonly ManualResetEventSlim _updated = new(false);

    public PanelStub(string url)
    {
        Model = new Mock<IContentModel>(MockBehavior.Loose);
        Model.Setup(m => m.Url).Returns(new Url(url));
        Model.Setup(m => m.ContentBrowser).Returns(Mock.Of<IContentBrowser>());
        Model.Setup(m => m.Refresh(It.IsAny<bool>())).Callback<bool>(forceRefresh =>
        {
            WasForced = forceRefresh;
            _refreshed.Set();
        });
        Model.Setup(m => m.Update()).Callback(() => _updated.Set());
    }

    public Mock<IContentModel> Model { get; }

    public IContentModel Object => Model.Object;

    /// <summary>The <c>forceRefresh</c> argument of the last <see cref="IContentModel.Refresh"/> call.</summary>
    public bool? WasForced { get; private set; }

    public bool WasRefreshed(TimeSpan timeout) => _refreshed.Wait(timeout);

    public bool WasUpdated(TimeSpan timeout) => _updated.Wait(timeout);

    /// <summary>Waits out <paramref name="grace"/> to give a refresh that should not happen a fair
    /// chance to prove the expectation wrong.</summary>
    public bool WasNotRefreshed(TimeSpan grace) => !_refreshed.Wait(grace);

    /// <summary>Makes the next <see cref="IContentModel.Refresh"/> throw, standing in for a dropped
    /// connection or a device unplugged mid-copy.</summary>
    public PanelStub ThatFailsToRefresh(Exception exception)
    {
        Model.Setup(m => m.Refresh(It.IsAny<bool>())).Callback(() =>
        {
            _refreshed.Set();
            throw exception;
        });
        return this;
    }
}
