using iConsole.Executables;
using iConsole.Formats.Executables;
using iConsole.Hdf;
using iConsole.Formats.Hdf;
using iConsole.Parquet;
using iConsole.Formats.Parquet;
using iConsole.Services;
using iConsole.Formats.Services;
using iConsole.Formats.Services.Disassembly;
using iConsole.Sqlite;
using iConsole.Formats.Sqlite;
using iConsole.Structured;
using Microsoft.Extensions.DependencyInjection;

namespace iConsole.Formats.Application;

public static class FileFormatsServiceCollectionExtension
{
    /// <summary>
    /// Registers the file-format services that the viewers and the editor resolve through their interfaces: the
    /// file-to-text converter, the YAML and TOML parser, the executable disassembler and analyzer, and the read-only
    /// readers behind the Parquet, HDF5 and SQLite viewers. The composition root calls it; iConsole itself does not
    /// know these implementations.
    /// </summary>
    public static IServiceCollection AddFileFormats(this IServiceCollection services)
    {
        services.AddSingleton<IFileToTextConverterService, FileToTextConverterService>();
        services.AddSingleton<IStructuredDocumentParser, StructuredDocumentParser>();
        services.AddSingleton<IExecutableDisassembler, ExecutableDisassembler>();
        // The viewers and the analysis view read a file through these readers; they hold no state, so each use gets a new one.
        services.AddTransient<IExecutableAnalyzer, ExecutableAnalyzer>();
        services.AddTransient<IHdfFileReader, Hdf5FileReader>();
        services.AddTransient<IParquetFileReader, ParquetFileReader>();
        services.AddTransient<ISqliteDatabaseReader, SqliteDatabaseReader>();
        return services;
    }
}
