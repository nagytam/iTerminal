using iConsole.Data;

namespace iTerminal.Core.Model.ContentModels;

public class MemoryBase(Url url)
{
    public Url Url { get; set; } = url;
    public DateTime CreationTime { get; set; } = DateTime.Now;
}