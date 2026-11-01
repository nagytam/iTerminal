namespace iTerminal.Core.Services;

/// <summary>How a copy or move answers a file that already exists at its target.</summary>
public enum FileConflictMode
{
    /// <summary>The "overwrite files that are already there" box is unticked: ask per file.</summary>
    Ask,

    /// <summary>The box is ticked: overwrite every conflict without asking.</summary>
    OverwriteAll,

    /// <summary>Paste of a file: an existing target is an error, as it has always been. Never asks.</summary>
    Legacy,
}
