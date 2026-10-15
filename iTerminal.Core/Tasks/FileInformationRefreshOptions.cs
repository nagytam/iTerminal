namespace iTerminal.Core.Tasks
{
    // Options for FileInformationRefreshTask injected via IOptions<FileInformationRefreshOptions>
    public sealed class FileInformationRefreshOptions
    {
        // Number of files between progress reports. Defaults to 50 to match prior behavior.
        public uint ReportBatchSize { get; set; } = 50;
    }
}
