using iTerminal.Core.Extensions;
using iTerminal.Widgets.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Widgets.Test.Application;

/// <summary>
/// The widgets reach the core menus only through the contracts in iTerminal.Core.Extensions. These checks read the
/// registrations, so a widget that is added to a menu or a key map without its registration fails here.
/// </summary>
[TestClass]
public class WidgetsApplicationBuilderExtensionTest
{
    private static int Count<TService>(IServiceCollection services) =>
        services.Count(descriptor => descriptor.ServiceType == typeof(TService));

    [Timeout(60_000)]
    [TestMethod]
    public void AddWidgetsContributesEveryToolsMenuEntryHotKeyIndicatorAndDialogName()
    {
        var services = new ServiceCollection();

        services.AddWidgets();

        Assert.AreEqual(9, Count<IToolsMenuContribution>(services), "the Tools menu entries of the widgets");
        Assert.AreEqual(3, Count<IStatusBarHotKey>(services), "the hot-key buttons of the widgets");
        Assert.AreEqual(2, Count<IMenuBarIndicator>(services), "the quote and weather indicators");
        Assert.AreEqual(4, Count<IOpenableDialogContribution>(services), "the dialogs the AI assistant opens by name");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AddWidgetsRegistersThisAssemblyForTheKeyMap()
    {
        var services = new ServiceCollection();

        services.AddWidgets();

        ServiceDescriptor descriptor = services.Single(d => d.ServiceType == typeof(ICommandAssemblyContribution));
        var contribution = (ICommandAssemblyContribution)descriptor.ImplementationInstance!;
        Assert.AreEqual(typeof(WidgetsApplicationBuilderExtension).Assembly, contribution.Assembly);
    }
}
