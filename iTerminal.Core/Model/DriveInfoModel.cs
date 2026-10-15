using iTerminal.Core.Data;

namespace iTerminal.Core.Model;

public class DriveInfoModel
{
    public virtual iConsole.Data.ConcurrentObservableCollection<Drive> Drives { get; private set; } = [];

    /// <summary>Set once <see cref="Jobs.DriveInfoJob"/> has finished its first pass: until then an empty list means "not read yet", not "no drives".</summary>
    public volatile bool IsLoaded;
}
