using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Model;

namespace iConsole.Services;

public interface IFileToTextConverterService
{
    Task ReadFileAsync(Url? url, IFileSystem fileSystem, ITextModel textModel);
}