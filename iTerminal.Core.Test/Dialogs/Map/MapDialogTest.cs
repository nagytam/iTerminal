using iConsole.Controls;
using iConsole.Data;
using iConsole.Test.Core;
using iTerminal.Core.Dialogs.Map;
using iTerminal.Core.Model.Map;
using iTerminal.Core.Services.Map;
using iTerminal.Core.Test.Model.Map;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Map;

[TestClass]
public class MapDialogTest
{
    private sealed class FakeSearch(Func<string, IReadOnlyList<MapPlace>> answer) : IMapSearchService
    {
        public List<string> Queries { get; } = [];

        public Task<IReadOnlyList<MapPlace>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(answer(query));
        }
    }

    private sealed class Fixture
    {
        public required MapDialog Dialog { get; init; }
        public required Control Root { get; init; }
        public required FakeTileFetcher Fetcher { get; init; }
        public required FakeSearch Search { get; init; }
        public required MapStore Store { get; init; }
        public MapModel Model => Dialog.Model;
    }

    private string _folder = string.Empty;

    [TestInitialize]
    public void Setup() => _folder = Path.Combine(Path.GetTempPath(), "iterminal-mapdialog-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        MapTestSupport.DeleteFolder(_folder);
    }

    private async Task<Fixture> OpenAsync(Func<string, IReadOnlyList<MapPlace>>? search = null, bool online = true)
    {
        var fetcher = new FakeTileFetcher(_ => MapTestSupport.Png(MapTestSupport.Water));
        var store = new MapStore(new MemorySettings()) { Online = online };
        var fake = new FakeSearch(search ?? (_ => []));
        var dialog = new MapDialog(new MapTileService(fetcher, store, _folder), fake, store, TestTheme());
        var root = await ShowAsync(dialog);
        return new Fixture { Dialog = dialog, Root = root, Fetcher = fetcher, Search = fake, Store = store };
    }

    private static async Task PressAsync(Fixture f, ConsoleKey key, char c = '\0', bool shift = false, bool alt = false, bool control = false) =>
        await f.Dialog.HandleKeyEventAsync(Key(key, c, shift, alt, control));

    private static async Task TypeTextAsync(Fixture f, string text)
    {
        foreach (var c in text)
        {
            await f.Dialog.HandleKeyEventAsync(Char(c));
        }
    }

    private static async Task SettleAsync(Fixture f)
    {
        for (var i = 0; i < 300; i++)
        {
            f.Dialog.OnIdle();
            Render(f.Root);
            if (f.Dialog.Frame is { Loading: 0 })
            {
                return;
            }

            await Task.Delay(10);
        }
    }

    [TestMethod]
    public async Task ShowsTheHeaderWithModeCoordinatesZoomAndAttribution()
    {
        var f = await OpenAsync();

        var screen = Render(f.Root);

        AssertScreenContains(screen, "ONLINE");
        AssertScreenContains(screen, "47.4979°N 19.0402°E");
        AssertScreenContains(screen, "zoom 12/19");
        AssertScreenContains(screen, "© OpenStreetMap contributors");
        AssertScreenContains(screen, "F1:help");
    }

    [TestMethod]
    public async Task DrawsTheMapAsAsciiOnceTheTilesHaveArrived()
    {
        var f = await OpenAsync();

        await SettleAsync(f);
        var screen = Render(f.Root);

        AssertScreenContains(screen, "~~~~~~~~");
        AssertScreenContains(screen, "X");
        AssertScreenContains(screen, "|-");
    }

    [TestMethod]
    public async Task OfflineModeShowsTheStateAndNeverFetches()
    {
        var f = await OpenAsync(online: false);

        await SettleAsync(f);
        var screen = Render(f.Root);

        AssertScreenContains(screen, "OFFLINE");
        AssertScreenContains(screen, "missing");
        Assert.AreEqual(0, f.Fetcher.Requests);
    }

    [TestMethod]
    public async Task OTogglesOnlineAndOffline()
    {
        var f = await OpenAsync();

        await PressAsync(f, ConsoleKey.O, 'o');
        Assert.IsFalse(f.Model.Online);
        AssertScreenContains(Render(f.Root), "Offline: only saved tiles");

        await PressAsync(f, ConsoleKey.O, 'o');
        Assert.IsTrue(f.Model.Online);
    }

    [TestMethod]
    public async Task ArrowsPanAndPlusMinusZoom()
    {
        var f = await OpenAsync();
        Render(f.Root);
        var longitude = f.Model.Longitude;
        var latitude = f.Model.Latitude;

        await PressAsync(f, ConsoleKey.RightArrow);
        Assert.IsTrue(f.Model.Longitude > longitude);
        await PressAsync(f, ConsoleKey.LeftArrow);
        await PressAsync(f, ConsoleKey.LeftArrow);
        Assert.IsTrue(f.Model.Longitude < longitude);
        await PressAsync(f, ConsoleKey.UpArrow);
        Assert.IsTrue(f.Model.Latitude > latitude);

        await PressAsync(f, ConsoleKey.Add, '+');
        Assert.AreEqual(13, f.Model.Zoom);
        await PressAsync(f, ConsoleKey.OemMinus, '-');
        await PressAsync(f, ConsoleKey.PageDown);
        Assert.AreEqual(11, f.Model.Zoom);
        await PressAsync(f, ConsoleKey.PageUp);
        Assert.AreEqual(12, f.Model.Zoom);
    }

    [TestMethod]
    public async Task ShiftArrowsPanHalfAScreen()
    {
        var f = await OpenAsync();
        Render(f.Root);
        var (x0, _) = MapProjection.ToPixel(f.Model.Latitude, f.Model.Longitude, f.Model.Zoom);

        await PressAsync(f, ConsoleKey.RightArrow, shift: true);

        var (x1, _) = MapProjection.ToPixel(f.Model.Latitude, f.Model.Longitude, f.Model.Zoom);
        Assert.AreEqual(118 / 2.0 * MapAsciiRenderer.CellWidth, x1 - x0, 1.0);
    }

    [TestMethod]
    public async Task ClickingTheMapCentresItOnThatSpot()
    {
        var f = await OpenAsync();
        Render(f.Root);
        var longitude = f.Model.Longitude;

        await f.Dialog.HandleMouseEventAsync(new MouseEvent { LeftClicked = true, Position = new Position(100, 12) });

        Assert.IsTrue(f.Model.Longitude > longitude);
    }

    [TestMethod]
    public async Task HomeReturnsToBudapest()
    {
        var f = await OpenAsync();
        f.Model.CenterOn(0, 0, 3);

        await PressAsync(f, ConsoleKey.Home);

        Assert.AreEqual(MapStore.Home.Latitude, f.Model.Latitude, 1e-6);
        Assert.AreEqual(MapStore.HomeZoom, f.Model.Zoom);
    }

    [TestMethod]
    public async Task TypingCoordinatesJumpsThereWithoutTheNetwork()
    {
        var f = await OpenAsync(search: q =>
        {
            MapProjection.TryParseCoordinates(q, out var latitude, out var longitude);
            return [new MapPlace(q, latitude, longitude)];
        }, online: false);

        await PressAsync(f, ConsoleKey.G, 'g');
        Assert.IsTrue(f.Dialog.IsTyping);
        AssertScreenContains(Render(f.Root), "Search or coordinates:");
        await TypeTextAsync(f, "35.68, 139.69");
        await PressAsync(f, ConsoleKey.Enter, '\r');
        await WaitUntil(f, () => Math.Abs(f.Model.Latitude - 35.68) < 1e-6);

        Assert.AreEqual(139.69, f.Model.Longitude, 1e-6);
        Assert.IsTrue(f.Model.Zoom >= 14);
    }

    [TestMethod]
    public async Task SearchingByNameOfflineExplainsItNeedsTheNetwork()
    {
        var f = await OpenAsync(online: false);

        await PressAsync(f, ConsoleKey.Oem2, '/');
        await TypeTextAsync(f, "Vienna");
        await PressAsync(f, ConsoleKey.Enter, '\r');

        AssertScreenContains(Render(f.Root), "needs the network");
        Assert.AreEqual(0, f.Search.Queries.Count);
    }

    [TestMethod]
    public async Task SeveralSearchResultsAreListedAndEnterGoesToTheChosenOne()
    {
        var f = await OpenAsync(search: _ =>
        [
            new MapPlace("Paris, France", 48.8566, 2.3522),
            new MapPlace("Paris, Texas", 33.6609, -95.5555),
        ]);

        await PressAsync(f, ConsoleKey.Oem2, '/');
        await TypeTextAsync(f, "Paris");
        await PressAsync(f, ConsoleKey.Enter, '\r');
        await WaitUntil(f, () => f.Dialog.ShowsList);

        var screen = Render(f.Root);
        AssertScreenContains(screen, "Search results (2)");
        AssertScreenContains(screen, "Paris, Texas");

        await PressAsync(f, ConsoleKey.DownArrow);
        await PressAsync(f, ConsoleKey.Enter, '\r');

        Assert.IsFalse(f.Dialog.ShowsList);
        Assert.AreEqual(33.6609, f.Model.Latitude, 1e-6);
        AssertScreenContains(Render(f.Root), "Paris, Texas");
    }

    [TestMethod]
    public async Task NothingFoundIsReported()
    {
        var f = await OpenAsync();

        await PressAsync(f, ConsoleKey.Oem2, '/');
        await TypeTextAsync(f, "zzzzzz");
        await PressAsync(f, ConsoleKey.Enter, '\r');
        await WaitUntil(f, () => f.Dialog.Status.Contains("Nothing found", StringComparison.Ordinal));

        AssertScreenContains(Render(f.Root), "Nothing found for");
    }

    [TestMethod]
    public async Task SavedPlacesAreListedGoneToAndRemoved()
    {
        var f = await OpenAsync();
        f.Model.CenterOn(10, 20, 9);
        await PressAsync(f, ConsoleKey.M, 'm');
        AssertScreenContains(Render(f.Root), "Saved: 10.0000°N 20.0000°E");
        f.Model.CenterOn(0, 0, 4);

        await PressAsync(f, ConsoleKey.B, 'b');
        AssertScreenContains(Render(f.Root), "Saved places (1)");
        await PressAsync(f, ConsoleKey.Enter, '\r');
        Assert.AreEqual(10, f.Model.Latitude, 1e-6);

        await PressAsync(f, ConsoleKey.B, 'b');
        await PressAsync(f, ConsoleKey.Delete);
        Assert.AreEqual(0, f.Store.Places.Count);
        Assert.IsFalse(f.Dialog.ShowsList);
    }

    [TestMethod]
    public async Task WithoutSavedPlacesBSaysSo()
    {
        var f = await OpenAsync();

        await PressAsync(f, ConsoleKey.B, 'b');

        AssertScreenContains(Render(f.Root), "No saved places yet");
    }

    [TestMethod]
    public async Task DSavesTheViewForOfflineUse()
    {
        var f = await OpenAsync();
        await SettleAsync(f);

        await PressAsync(f, ConsoleKey.D, 'd');
        await WaitUntil(f, () => f.Model.Message.StartsWith("Saved", StringComparison.Ordinal));

        Assert.IsTrue(Directory.Exists(Path.Combine(_folder, "tiles")));
    }

    [TestMethod]
    public async Task F1ShowsHelpAndAnyKeyGoesBack()
    {
        var f = await OpenAsync();

        await PressAsync(f, ConsoleKey.F1);
        Assert.IsTrue(f.Dialog.ShowsHelp);
        AssertScreenContains(Render(f.Root), "OpenStreetMap as ASCII art");

        await PressAsync(f, ConsoleKey.Spacebar, ' ');
        Assert.IsFalse(f.Dialog.ShowsHelp);
    }

    [TestMethod]
    public async Task EscClosesTheDialog()
    {
        var f = await OpenAsync();

        await PressAsync(f, ConsoleKey.Escape);

        Assert.IsFalse(f.Root.Controls.AsSnapshot().Contains(f.Dialog));
    }

    [TestMethod]
    public async Task MouseWheelZoomsAtThePointer()
    {
        var f = await OpenAsync();
        Render(f.Root);

        await f.Dialog.HandleMouseEventAsync(new MouseEvent { WheelDelta = 1, Position = new Position(60, 20) });
        Assert.AreEqual(13, f.Model.Zoom);

        await f.Dialog.HandleMouseEventAsync(new MouseEvent { WheelDelta = -1, Position = new Position(60, 20) });
        Assert.AreEqual(12, f.Model.Zoom);
    }

    [TestMethod]
    public async Task ViewIsRememberedBetweenOpens()
    {
        var f = await OpenAsync();
        await PressAsync(f, ConsoleKey.Add, '+');

        var (_, _, zoom) = f.Store.LoadView();

        Assert.AreEqual(13, zoom);
    }

    private static async Task WaitUntil(Fixture f, Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            f.Dialog.OnIdle();
            await Task.Delay(10);
        }

        Assert.IsTrue(condition(), "timed out waiting");
    }
}
