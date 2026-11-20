using System.Resources;
using iConsole.Localization;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.SystemInfo;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

[TestClass]
public class SystemInfoDialogTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task OpensAtOnce_ShowsThePlaceholder_AndWatchesTheSampler()
    {
        var sampler = new FakeSampler();
        var dialog = new SystemInfoDialog(TestTheme(), sampler, Localizer("en"));

        await ShowAsync(dialog);

        CollectionAssert.Contains(dialog.LinesForTest.ToList(), SystemInfoDialog.CollectingText);
        Assert.AreEqual(1, sampler.Watching);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheReport_ShowsEachLabelOnce()
    {
        var sampler = new FakeSampler { Current = Sample() };
        var dialog = new SystemInfoDialog(TestTheme(), sampler, Localizer("en"));

        await ShowAsync(dialog);
        dialog.OnIdle();

        var lines = dialog.LinesForTest;
        Assert.AreEqual(1, lines.Count(line => line.StartsWith("  OS Description", StringComparison.Ordinal)));
        Assert.AreEqual(1, lines.Count(line => line.StartsWith("  Logical processors", StringComparison.Ordinal)));
        Assert.AreEqual(1, lines.Count(line => line.StartsWith("  Available Memory", StringComparison.Ordinal)));
        Assert.AreEqual(1, lines.Count(line => line.StartsWith("  Physical Memory (total)", StringComparison.Ordinal)));
        Assert.IsTrue(lines.Any(line => line.Contains("Test OS")));
        Assert.IsTrue(lines.Any(line => line.Contains("Disk I/O")));
        Assert.IsTrue(lines.Any(line => line.Contains("Network I/O")));
        Assert.IsTrue(lines.Any(line => line.Contains("Environment Variables")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Close_DropsTheWatchHandle_OnlyOnce()
    {
        var sampler = new FakeSampler();
        var dialog = new SystemInfoDialog(TestTheme(), sampler, Localizer("en"));
        await ShowAsync(dialog);

        dialog.Close();
        dialog.Close();

        Assert.AreEqual(0, sampler.Watching);
        Assert.AreEqual(1, sampler.HandlesDisposed);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ALanguageChange_ReformatsTheSnapshot_WithoutAnotherProbe()
    {
        var localizer = Localizer("en");
        var sampler = new FakeSampler { Current = Sample() with { CpuModel = Probed<string>.NotReported } };
        var dialog = new SystemInfoDialog(TestTheme(), sampler, localizer);
        await ShowAsync(dialog);
        dialog.OnIdle();
        Assert.IsTrue(dialog.LinesForTest.Any(line => line.Contains("not reported")), "the English status word");

        Assert.IsTrue(localizer.SetLanguage("de"));
        dialog.OnIdle();

        var lines = dialog.LinesForTest;
        Assert.IsTrue(lines.Any(line => line.Contains("Betriebssystem")), "the German section header");
        Assert.IsTrue(lines.Any(line => line.Contains("nicht gemeldet")), "the German status word");
        Assert.AreEqual(1, sampler.Watching, "the language change does not start or stop the watch");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheMemoryStrip_IsFedFromSnapshotsOnly()
    {
        var sampler = new FakeSampler { Current = Sample() };
        var dialog = new SystemInfoDialog(TestTheme(), sampler, Localizer("en"));
        await ShowAsync(dialog);

        dialog.OnIdle();
        dialog.OnIdle();
        Assert.AreEqual(1, dialog.MemoryHistoryForTest.Count, "the same snapshot is not counted twice");
        Assert.AreEqual(0.5, dialog.MemoryHistoryForTest[0], 0.0001, "half of the memory is in use");

        sampler.Current = Sample() with { PhysicalMemoryAvailable = Probed<ulong>.Ok(4UL * 1024 * 1024 * 1024) };
        dialog.OnIdle();
        Assert.AreEqual(2, dialog.MemoryHistoryForTest.Count);
        Assert.AreEqual(0.75, dialog.MemoryHistoryForTest[1], 0.0001);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedSample_ShowsAStaleNotice_UntilTheNextGoodSample()
    {
        var sampler = new FakeSampler { Current = Sample() };
        var dialog = new SystemInfoDialog(TestTheme(), sampler, Localizer("en"));
        await ShowAsync(dialog);
        dialog.OnIdle();
        Assert.IsFalse(dialog.LinesForTest.Any(line => line.Contains("out of date")));

        sampler.PassFailing = true;
        dialog.OnIdle();
        Assert.IsTrue(dialog.LinesForTest.Any(line => line.Contains("out of date")), "the values may be old, and the report says so");

        sampler.PassFailing = false;
        dialog.OnIdle();
        Assert.IsFalse(dialog.LinesForTest.Any(line => line.Contains("out of date")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PassesFailingBeforeAnySnapshot_ShowTheNoticeUnderThePlaceholder()
    {
        var sampler = new FakeSampler { PassFailing = true };
        var dialog = new SystemInfoDialog(TestTheme(), sampler, Localizer("en"));
        await ShowAsync(dialog);

        dialog.OnIdle();

        var lines = dialog.LinesForTest;
        CollectionAssert.Contains(lines.ToList(), SystemInfoDialog.CollectingText);
        Assert.IsTrue(lines.Any(line => line.Contains("out of date")), "the failure is shown, not left as 'collecting' for good");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutASnapshot_TheDialogKeepsItsPlaceholder()
    {
        var dialog = new SystemInfoDialog(TestTheme(), new FakeSampler(), Localizer("en"));
        await ShowAsync(dialog);

        dialog.OnIdle();

        CollectionAssert.Contains(dialog.LinesForTest.ToList(), SystemInfoDialog.CollectingText);
    }

    private static ILocalizer Localizer(string code)
    {
        var localizer = new iConsole.Localization.Localizer(
        [
            new ResourceLocalizationSource(new ResourceManager("iConsole.Localization.Resources.Strings", typeof(iConsole.Localization.Localizer).Assembly)),
            new ResourceLocalizationSource(new ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(SystemInfoDialog).Assembly)),
        ]);
        Assert.IsTrue(localizer.SetLanguage(code), code);
        return localizer;
    }

    private static SystemSnapshot Sample()
    {
        Probed<T> Ok<T>(T value) => Probed<T>.Ok(value);
        Probed<T> None<T>() => Probed<T>.NotReported;

        return new SystemSnapshot(
            Ok("Test OS"),
            Ok("X64"),
            Ok(".NET test"),
            Ok("tester"),
            Ok("TESTS"),
            Ok("/home/tester"),
            Ok<IReadOnlyList<KeyValuePair<string, string>>>([new("PATH", "/bin")]),
            Ok("Test CPU"),
            Ok(4),
            Ok(12.5),
            Ok(16UL * 1024 * 1024 * 1024),
            Ok(8UL * 1024 * 1024 * 1024),
            None<IReadOnlyList<GpuReading>>(),
            None<double>(),
            Ok<IReadOnlyList<string>>([]),
            [],
            None<IReadOnlyList<InterfaceReading>>(),
            Ok<IReadOnlyList<string>>([]),
            [],
            Ok<IReadOnlyList<string>>([]),
            [],
            Ok<IReadOnlyList<string>>([]),
            []);
    }

    /// <summary>A sampler the test fills in: it holds one snapshot, and counts the watches it hands out and releases.</summary>
    private sealed class FakeSampler : ISystemSampler
    {
        private int _snapshotReads;
        private SystemSnapshot? _current;

        public SystemSnapshot? Current
        {
            get
            {
                _snapshotReads++;
                return _current;
            }
            set => _current = value;
        }

        public bool PassFailing { get; set; }

        public int Watching { get; private set; }

        public int HandlesDisposed { get; private set; }

        public int SnapshotReads => _snapshotReads;

        public IDisposable Watch()
        {
            Watching++;
            return new Handle(this);
        }

        public void Dispose()
        {
        }

        private sealed class Handle(FakeSampler owner) : IDisposable
        {
            public void Dispose()
            {
                owner.Watching--;
                owner.HandlesDisposed++;
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("en", 18)]
    [DataRow("de", 28)]
    public async Task EveryValueStartsInTheSameColumn_WhateverTheLanguagesLongestLabelIs(string language, int minimumColumn)
    {
        // The label column was a fixed 18: German "Physischer Speicher (gesamt)" pushed its value out of line with the rows above it.
        var sampler = new FakeSampler { Current = Sample() };
        var dialog = new SystemInfoDialog(TestTheme(), sampler, Localizer(language));
        await ShowAsync(dialog);
        dialog.OnIdle();

        var columns = dialog.LinesForTest
            .Where(line => line.StartsWith("  ", StringComparison.Ordinal) && !line.StartsWith("   ", StringComparison.Ordinal) && line.Contains(": ", StringComparison.Ordinal))
            .Select(line => line.IndexOf(": ", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        Assert.AreEqual(1, columns.Count, "the colons are in columns " + string.Join(", ", columns));
        Assert.IsTrue(columns[0] >= minimumColumn, $"the column is {columns[0]}");
    }
}
