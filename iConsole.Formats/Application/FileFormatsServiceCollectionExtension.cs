using iConsole.Certificates;
using iConsole.Formats.Certificates;
using iConsole.Csv;
using iConsole.Formats.Csv;
using iConsole.Executables;
using iConsole.Formats.Executables;
using iConsole.Formats.Har;
using iConsole.Har;
using iConsole.Hdf;
using iConsole.Formats.Hdf;
using iConsole.Json;
using iConsole.Formats.Json;
using iConsole.Markdown;
using iConsole.Formats.Markdown;
using iConsole.Parquet;
using iConsole.Formats.Parquet;
using iConsole.Services;
using iConsole.Formats.Services;
using iConsole.Formats.Services.Disassembly;
using iConsole.Sqlite;
using iConsole.Formats.Sqlite;
using iConsole.Structured;
using iConsole.Xml;
using iConsole.Formats.Xml;
using iConsole.Xps;
using iConsole.Formats.Xps;
using Microsoft.Extensions.DependencyInjection;

namespace iConsole.Formats.Application;

public static class FileFormatsServiceCollectionExtension
{
    /// <summary>
    /// Registers the file-format services that the viewers and the editor resolve through their interfaces: the
    /// file-to-text converter, the YAML and TOML parser, the executable disassembler and analyzer, the parsers and readers
    /// behind the certificate, CSV, HAR, JSON, XML, XPS and Markdown viewers, and the read-only readers behind the Parquet,
    /// HDF5 and SQLite viewers. The composition root calls it; iConsole itself does not know these implementations.
    /// </summary>
    public static IServiceCollection AddFileFormats(this IServiceCollection services)
    {
        services.AddSingleton<IFileToTextConverterService, FileToTextConverterService>();
        services.AddSingleton<IStructuredDocumentParser, StructuredDocumentParser>();
        // The readers and parsers behind the viewers. They hold no state, so one instance serves every viewer.
        services.AddSingleton<ICertificateReader, CertificateReader>();
        services.AddSingleton<ICsvDocumentParser, CsvDocumentParser>();
        services.AddSingleton<IHarDocumentParser, HarDocumentParser>();
        services.AddSingleton<IJsonValidator, JsonValidator>();
        services.AddSingleton<IXmlDocumentParser, XmlDocumentParser>();
        services.AddSingleton<IXmlValidator, XmlValidator>();
        services.AddSingleton<IXpsDocumentReader, XpsDocumentReader>();
        services.AddSingleton<IMarkdownParser, MarkdownParser>();
        services.AddSingleton<IExecutableDisassembler, ExecutableDisassembler>();
        // The viewers and the analysis view read a file through these readers; they hold no state, so each use gets a new one.
        services.AddTransient<IExecutableAnalyzer, ExecutableAnalyzer>();
        services.AddTransient<IHdfFileReader, Hdf5FileReader>();
        services.AddTransient<IParquetFileReader, ParquetFileReader>();
        services.AddTransient<ISqliteDatabaseReader, SqliteDatabaseReader>();
        return services;
    }
}
