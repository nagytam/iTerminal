using iConsole.Executables;
using iConsole.Services;
using iConsole.Services.Disassembly;
using iConsole.Structured;
using Microsoft.Extensions.DependencyInjection;

namespace iConsole.Formats.Application;

public static class FileFormatsServiceCollectionExtension
{
    /// <summary>
    /// Registers the file-format services that the viewers and the editor resolve through their interfaces: the
    /// file-to-text converter, the YAML and TOML parser and the executable disassembler. The composition root calls it;
    /// iConsole itself does not know these implementations.
    /// </summary>
    public static IServiceCollection AddFileFormats(this IServiceCollection services)
    {
        services.AddSingleton<IFileToTextConverterService, FileToTextConverterService>();
        services.AddSingleton<IStructuredDocumentParser, StructuredDocumentParser>();
        services.AddSingleton<IExecutableDisassembler, ExecutableDisassembler>();
        return services;
    }
}
