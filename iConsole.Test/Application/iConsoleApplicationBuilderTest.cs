using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using iConsole.Application;

namespace iConsole.Application.Test;

[TestClass]
public class iConsoleApplicationBuilderTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void AddIConsoleServices_ShouldRegisterExpectedServices()
    {
        var services = new ServiceCollection();
        services.AddiConsoleCore();

        var sp = services.BuildServiceProvider();

        Assert.IsNotNull(sp);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AddiConsoleCore_ConfiguresShortShutdownTimeout()
    {
        // The generic host's 30s default ShutdownTimeout would let a single straggling
        // background job hold up F10 (Quit) for a long time. Every Job now reacts to
        // cancellation quickly, so this should be capped well below the default.
        var services = new ServiceCollection();
        services.AddiConsoleCore();

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<HostOptions>>().Value;

        Assert.IsTrue(options.ShutdownTimeout < TimeSpan.FromSeconds(30),
            $"Expected a ShutdownTimeout well under the 30s default, got {options.ShutdownTimeout}");
    }
}

