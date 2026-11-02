using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using iTerminal.Core.AI;
using iTerminal.Core.Application;
using iTerminal.Core.Data;
using iTerminal.Core.Factories;
using iTerminal.Core.Services.RuntimeDependencies;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace iTerminal.Core.Test.Application;

[TestClass]
public class StartupWarmUpTest
{
    private static FilePanelFactory UnbuiltPanelFactory() =>
        (FilePanelFactory)RuntimeHelpers.GetUninitializedObject(typeof(FilePanelFactory));

    [Timeout(30_000)]
    [TestMethod]
    public void TheSlowServicesAreBuiltOffTheCallingThread_AndStartDoesNotWaitForThem()
    {
        using ManualResetEventSlim release = new();
        ConcurrentBag<int> threads = [];
        using CountdownEvent built = new(3);
        ServiceCollection services = new();
        services.AddSingleton<IRuntimeDependencyService>(_ => Build(Mock.Of<IRuntimeDependencyService>()));
        services.AddSingleton<IAiChatService>(_ => Build(Mock.Of<IAiChatService>()));
        services.AddSingleton<FilePanelFactory>(_ => Build(UnbuiltPanelFactory()));
        using ServiceProvider provider = services.BuildServiceProvider();

        T Build<T>(T service)
        {
            threads.Add(Environment.CurrentManagedThreadId);
            release.Wait();
            built.Signal();
            return service;
        }

        StartupWarmUp.Start(provider);

        Assert.IsFalse(built.IsSet, "Start returns while the services are still being built.");
        release.Set();
        Assert.IsTrue(built.Wait(TimeSpan.FromSeconds(20)), "All three are built in the background.");
        Assert.IsFalse(threads.Contains(Environment.CurrentManagedThreadId), "None of them is built on the calling thread.");
    }

    [Timeout(30_000)]
    [TestMethod]
    public void AServiceThatFailsToBuildDoesNotStopTheOthersOrThrowFromStart()
    {
        using CountdownEvent built = new(2);
        ServiceCollection services = new();
        services.AddSingleton<IRuntimeDependencyService>(_ => throw new InvalidOperationException("construction failed"));
        services.AddSingleton<IAiChatService>(_ =>
        {
            built.Signal();
            return Mock.Of<IAiChatService>();
        });
        services.AddSingleton<FilePanelFactory>(_ =>
        {
            built.Signal();
            return UnbuiltPanelFactory();
        });
        using ServiceProvider provider = services.BuildServiceProvider();

        StartupWarmUp.Start(provider);

        Assert.IsTrue(built.Wait(TimeSpan.FromSeconds(20)), "The services that can be built still are.");
        Assert.ThrowsExactly<InvalidOperationException>(
            () => provider.GetRequiredService<IRuntimeDependencyService>(),
            "The failure is reported again, to whoever really needs the service.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheDatabaseWarmUpFinishesWithoutThrowing()
    {
        await DatabaseWarmUp.Start().WaitAsync(TimeSpan.FromSeconds(50));
    }
}
