using iConsole.Application;
using Microsoft.Extensions.Hosting;
using Moq;

namespace iConsole.Test.Application;

[TestClass]
public class iConsoleApplicationTest
{
    [TestMethod]
    public async Task RunAsync_StartsAndStopsHost()
    {
        var lifetime = new Mock<IHostApplicationLifetime>();
        var startedCts = new CancellationTokenSource();
        var stoppingCts = new CancellationTokenSource();
        lifetime.Setup(l => l.ApplicationStarted).Returns(startedCts.Token);
        lifetime.Setup(l => l.ApplicationStopping).Returns(stoppingCts.Token);

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(sp => sp.GetService(typeof(IHostApplicationLifetime)))
            .Returns(lifetime.Object);

        var host = new Mock<IHost>(MockBehavior.Strict);
        host.Setup(h => h.StartAsync(It.IsAny<CancellationToken>()))
            .Callback(() => { startedCts.Cancel(); stoppingCts.Cancel(); })
            .Returns(Task.CompletedTask);
        host.Setup(h => h.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        host.Setup(h => h.Services).Returns(serviceProvider.Object);
        host.Setup(h => h.Dispose());

        var app = new iConsoleApplication(host.Object);

        await app.RunAsync();

        host.Verify(h => h.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.Verify(h => h.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public void CreateBuilder_ReturnsBuilder()
    {
        var builder = iConsoleApplication.CreateBuilder([]);
        Assert.IsNotNull(builder);
    }
}
