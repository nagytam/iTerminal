using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Evidence;
using Serilog;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// A row of the <c>evidence://</c> drive: one entry of the notebook, named by its content file, dated
/// by when it was captured. The footer says where it came from; see <see cref="ContentModelEvidenceNotebook"/>.
/// </summary>
public record EvidenceRow : VirtualDriveEntry
{
    public EvidenceRow(Url url, EvidenceRecord record)
        : base(url, null, record.Size.ToFileSize(), record.Origin.CapturedUtc.ToLocalTime())
    {
        Record = record;
        Size = record.Size;
    }

    public EvidenceRecord Record { get; }
}

/// <summary>
/// A panel on the <c>evidence://</c> drive (Ctrl+Shift+Q opens the notebook here): what has been gathered
/// into the notebook folder, in the order it was added. Enter, F3, quick view and copying out read an
/// entry where it lies; nothing on the drive can be changed, deleted or renamed. New entries come from
/// the add command (Ctrl+Shift+X), which records the origin as it goes.
///
/// <para>The footer names the origin of the row under the cursor: where it was read from, which machine
/// and account gathered it, and when.</para>
/// </summary>
public class ContentModelEvidenceNotebook(
    string name,
    ISettingsService? settingsService,
    IEvidenceNotebookService notebook,
    FolderHistoryService? folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    ILogger? logger = null)
    : ContentModelAsyncDrive(name, settingsService, folderHistoryService, frequentExecutionGuardService, logger)
{
    protected override string Scheme => Container.EVIDENCE;

    public override ContainerType ContainerType => ContainerType.EvidenceNotebook;

    public override IFileSystem FileSystem => notebook;

    protected override async Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken)
    {
        var records = await notebook.ListAsync(cancellationToken);
        return [.. records.Select(record => (IEntry)new EvidenceRow(new Url(Container.EVIDENCE + record.FileName), record))];
    }

    protected override string DescribeLocation() => notebook.FolderPath is { } folder
        ? string.Format(UiText.Get("Evidence.Drive.Title", "Evidence notebook: {0}"), folder)
        : UiText.Get("Evidence.Drive.NoNotebook", "Evidence notebook (none open)");

    /// <summary>The drive is one flat list: wherever a URL on it points, <c>..</c> leaves the drive.</summary>
    protected override bool IsDriveRoot(Url url) => true;

    public override string GetFooter()
    {
        var footer = base.GetFooter();
        if (StatusMessage.Length > 0 || GetFocusedEntry() is not EvidenceRow row)
        {
            return footer;
        }

        var origin = row.Record.Origin;
        var captured = origin.CapturedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        return footer + "│ " + string.Format(
            UiText.Get("Evidence.Drive.Origin", "{0} from {1} · {2} · {3}"),
            row.Record.Kind,
            origin.SourceUrl,
            origin.Machine,
            captured) + " ";
    }
}
