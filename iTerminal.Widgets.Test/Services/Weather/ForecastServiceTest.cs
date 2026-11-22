using iTerminal.Core.Services;
using iTerminal.Widgets.Services.Weather;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Widgets.Test.Services.Weather;

/// <summary>The saved places, the home place and each place's forecast: kept across a restart, kept when a download fails, and only the newest request lands.</summary>
[TestClass]
public class ForecastServiceTest
{
    private sealed class MemorySettings : ISettingsService
    {
        private readonly Dictionary<string, string> _values = [];
        public bool IsDirty => false;
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public bool? GetAsBool(string key, bool? defaultValue = false) => defaultValue;
        public void Set(string key, string value) => _values[key] = value;
        public void Write() { }
    }

    private sealed class MovableTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Answers at once by default; with <see cref="Immediate"/> false each fetch waits in <see cref="Pending"/> until the test completes it.</summary>
    private sealed class FakeProvider : IForecastProvider
    {
        public bool Immediate { get; set; } = true;
        public Exception? Failure { get; set; }
        public List<TaskCompletionSource<Forecast>> Pending { get; } = [];
        public int FetchCount { get; private set; }

        public Task<Forecast> FetchAsync(SavedPlace place, CancellationToken cancellationToken = default)
        {
            FetchCount++;
            if (Failure is not null)
            {
                return Task.FromException<Forecast>(Failure);
            }

            if (Immediate)
            {
                return Task.FromResult(Make(10));
            }

            var waiting = new TaskCompletionSource<Forecast>();
            Pending.Add(waiting);
            return waiting.Task;
        }

        public Task<IReadOnlyList<PlaceHit>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlaceHit>>([]);
    }

    private static Forecast Make(double temperature) => new(
        new CurrentConditions(new DateTime(2026, 11, 18, 10, 0, 0), temperature, 2, true),
        [],
        [],
        new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero));

    private static readonly SavedPlace Budapest = new("Budapest", "Budapest", "Hungary", 47.4979, 19.0402, "Europe/Budapest");
    private static readonly SavedPlace Georgia = new("Budapest", "Georgia", "United States", 33.7151, -85.2180, "America/New_York");

    [TestMethod]
    public async Task ARePlacedAfterAFailedAttempt_IsFetchedAtOnce()
    {
        var provider = new FakeProvider { Failure = new HttpRequestException("offline") };
        var service = new ForecastService(new MemorySettings(), provider, new MovableTime());
        service.AddPlace(Budapest);
        await AssertAsync.ThrowsAsync<HttpRequestException>(() => service.RefreshAsync(Budapest));

        service.RemovePlace(Budapest);
        service.AddPlace(Budapest);

        Assert.IsTrue(service.NeedsRefresh(Budapest), "the old attempt does not hold back a place saved again");
    }

    [TestMethod]
    public void AForecastMissingItsLists_IsDroppedOnLoadRatherThanDrawn()
    {
        var settings = new MemorySettings();
        settings.Set(ForecastService.ForecastsKey, $"{{\"{Budapest.Key}\":{{\"current\":null,\"hourly\":null,\"daily\":null,\"fetchedAt\":\"2026-11-18T10:00:00+00:00\"}}}}");

        var service = new ForecastService(settings, new FakeProvider(), new MovableTime());

        Assert.IsNull(service.GetForecast(Budapest));
    }

    [TestMethod]
    public void AddPlace_SavesOnceAndRefusesADuplicate()
    {
        var service = new ForecastService(new MemorySettings(), new FakeProvider(), new MovableTime());

        Assert.IsTrue(service.AddPlace(Budapest));
        Assert.IsFalse(service.AddPlace(Budapest), "the same place twice");
        Assert.IsTrue(service.AddPlace(Georgia), "a place with the same name is a different place");
        Assert.AreEqual(2, service.Places.Count);
    }

    [TestMethod]
    public void RemovePlace_DropsItsForecastAndALostHome()
    {
        var provider = new FakeProvider();
        var service = new ForecastService(new MemorySettings(), provider, new MovableTime());
        service.AddPlace(Budapest);
        service.SetHome(Budapest);
        service.RefreshAsync(Budapest).Wait();

        Assert.IsTrue(service.RemovePlace(Budapest));

        Assert.AreEqual(0, service.Places.Count);
        Assert.IsNull(service.Home);
        Assert.IsNull(service.GetForecast(Budapest));
        Assert.IsFalse(service.RemovePlace(Budapest), "a place that is not saved");
    }

    [TestMethod]
    public void SetHome_MakesASavedPlaceHomeAndIgnoresAnUnsavedOne()
    {
        var service = new ForecastService(new MemorySettings(), new FakeProvider(), new MovableTime());
        service.AddPlace(Budapest);
        service.AddPlace(Georgia);

        service.SetHome(Georgia);
        service.SetHome(new SavedPlace("Nowhere", "", "", 0, 0, "UTC"));

        Assert.AreEqual(Georgia.Key, service.Home?.Key);
    }

    [TestMethod]
    public async Task PlacesHomeAndForecasts_SurviveARestart()
    {
        var settings = new MemorySettings();
        var first = new ForecastService(settings, new FakeProvider(), new MovableTime());
        first.AddPlace(Budapest);
        first.SetHome(Budapest);
        await first.RefreshAsync(Budapest);

        var second = new ForecastService(settings, new FakeProvider(), new MovableTime());

        Assert.AreEqual(1, second.Places.Count);
        Assert.AreEqual(Budapest.Key, second.Home?.Key);
        Assert.AreEqual(10, second.GetForecast(Budapest)?.Current.TemperatureC);
    }

    [TestMethod]
    public async Task AFailedDownload_KeepsTheLastForecast()
    {
        var provider = new FakeProvider();
        var service = new ForecastService(new MemorySettings(), provider, new MovableTime());
        service.AddPlace(Budapest);
        await service.RefreshAsync(Budapest);
        var kept = service.GetForecast(Budapest);

        provider.Failure = new HttpRequestException("offline");
        await AssertAsync.ThrowsAsync<HttpRequestException>(() => service.RefreshAsync(Budapest));

        Assert.AreSame(kept, service.GetForecast(Budapest), "the failed download did not replace it");
    }

    [TestMethod]
    public async Task NeedsRefresh_IsFalseWhileFreshAndTrueOnceOld()
    {
        var time = new MovableTime();
        var service = new ForecastService(new MemorySettings(), new FakeProvider(), time);
        service.AddPlace(Budapest);
        Assert.IsTrue(service.NeedsRefresh(Budapest), "no forecast yet");

        await service.RefreshAsync(Budapest);
        Assert.IsFalse(service.NeedsRefresh(Budapest));

        time.Now = time.Now.AddMinutes(16);
        Assert.IsTrue(service.NeedsRefresh(Budapest));
    }

    [TestMethod]
    public async Task NeedsRefresh_DoesNotRetryAFailureAtOnceEveryIdlePass()
    {
        var time = new MovableTime();
        var provider = new FakeProvider { Failure = new HttpRequestException("offline") };
        var service = new ForecastService(new MemorySettings(), provider, time);
        service.AddPlace(Budapest);
        await AssertAsync.ThrowsAsync<HttpRequestException>(() => service.RefreshAsync(Budapest));

        Assert.IsFalse(service.NeedsRefresh(Budapest), "the attempt was just now");
        time.Now = time.Now.AddMinutes(16);
        Assert.IsTrue(service.NeedsRefresh(Budapest));
    }

    [TestMethod]
    public async Task OnlyTheNewestRequestLands()
    {
        var provider = new FakeProvider { Immediate = false };
        var service = new ForecastService(new MemorySettings(), provider, new MovableTime());
        service.AddPlace(Budapest);

        var older = service.RefreshAsync(Budapest);
        var newer = service.RefreshAsync(Budapest);
        provider.Pending[1].SetResult(Make(2));
        await newer;
        provider.Pending[0].SetResult(Make(1));
        await older;

        Assert.AreEqual(2, service.GetForecast(Budapest)?.Current.TemperatureC, "the older request finished late and was dropped");
    }

    [TestMethod]
    public async Task ARequestForAPlaceRemovedWhileItRan_IsNotKept()
    {
        var provider = new FakeProvider { Immediate = false };
        var service = new ForecastService(new MemorySettings(), provider, new MovableTime());
        service.AddPlace(Budapest);

        var request = service.RefreshAsync(Budapest);
        service.RemovePlace(Budapest);
        provider.Pending[0].SetResult(Make(5));
        await request;

        Assert.IsNull(service.GetForecast(Budapest));
    }
}
