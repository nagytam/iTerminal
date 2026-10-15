using System.ComponentModel;
using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Model.ContentModels;
using Moq;

namespace iTerminal.Core.Test.Helper;

/// <summary>
/// A panel model over a real folder — <c>..</c>, <c>photos/</c>, <c>a.txt</c> (4 bytes),
/// <c>b.txt</c> (2 bytes), <c>c.jpg</c> — with <see cref="ContentModelBase"/>'s marking and nothing
/// else. Real files because whether a row is a folder, and how big a file is, come from its URL.
/// </summary>
internal sealed class MarkableTestModel : ContentModelBase
{
    private sealed class TestEntry(Url url, string name) : IEntry
    {
        public string FileSize => string.Empty;
        public string Date => string.Empty;
        public string Time => string.Empty;
        public string DisplayName => name;
        public string Name => name;
        public string FullName => url.FullName;
        public Url Url => url;
        public string DirectoryName => string.Empty;
        public string? NumberOfFiles { get; set; }
        public bool Exists => true;
#pragma warning disable CS0067
        public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067
        public string GetDisplayName(int width) => name;
        public string GetDisplayFileSize(int width) => string.Empty;
        public ConxelString FormatItemName(int width, bool cursor, ColorScheme colorScheme, VisualState visualState)
            => new(name.PadRight(width), colorScheme.Foreground, colorScheme.Background);
    }

    private MarkableTestModel(IFrequentExecutionGuardService guard, Url url)
        : base("Test", guard)
    {
        Url = url;
        ContentBrowser = Mock.Of<IContentBrowser>();
        ViewSize = new Size(40, 10);
    }

    /// <summary>Seeds <paramref name="root"/> with the files and lists them, cursor on <c>..</c>.</summary>
    public static MarkableTestModel Create(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "photos"));
        File.WriteAllText(Path.Combine(root, "a.txt"), "aaaa");
        File.WriteAllText(Path.Combine(root, "b.txt"), "bb");
        File.WriteAllText(Path.Combine(root, "c.jpg"), "c");

        Mock<IFrequentExecutionGuardService> guard = new();
        guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(a => a());
        var model = new MarkableTestModel(guard.Object, new Url(root + Path.DirectorySeparatorChar));
        model.Entries.Add(new TestEntry(new Url(Path.GetDirectoryName(root)! + Path.DirectorySeparatorChar), ".."));
        model.Entries.Add(new TestEntry(new Url(Path.Combine(root, "photos") + Path.DirectorySeparatorChar), "photos"));
        foreach (var name in new[] { "a.txt", "b.txt", "c.jpg" })
        {
            model.Entries.Add(new TestEntry(new Url(Path.Combine(root, name)), name));
        }

        model.Update();
        return model;
    }

    public IEntry Row(string name) => FilteredEntries.Single(e => e.DisplayName == name);

    /// <summary>Writes <paramref name="name"/> into the folder and lists it at the end.</summary>
    public IEntry AddFile(string name, string content)
    {
        var path = Path.Combine(Url.FullName.TrimEnd(Path.DirectorySeparatorChar), name);
        File.WriteAllText(path, content);
        var entry = new TestEntry(new Url(path), name);
        Entries.Add(entry);
        Update();
        return entry;
    }

    public string[] MarkedNames => [.. GetMarkedEntries().Select(e => e.DisplayName)];

    public override bool IsReadOnly => false;
    public override string GetFooter() => EntryCountFooter();
    public override string GetHeader(int width) => string.Empty;
    public override void ReadUrl() { }
    public override void Refresh(bool forceRefresh = false) { }
    public override void SetUrl(Url path)
    {
        // As every drive does.
        Url = path;
        ResetNavigation();
    }
    public override bool ExistsFile(Url url) => File.Exists(url.FullName);
    public override bool ExistsFolder(Url url) => Directory.Exists(url.FullName.TrimEnd('/', '\\'));
}
