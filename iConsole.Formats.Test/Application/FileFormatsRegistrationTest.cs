using iConsole.Certificates;
using iConsole.Csv;
using iConsole.Formats.Application;
using iConsole.Har;
using iConsole.Json;
using iConsole.Markdown;
using iConsole.Xml;
using iConsole.Xps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Formats.Test.Application;

[TestClass]
public class FileFormatsRegistrationTest
{
    [TestMethod]
    [Timeout(60_000)]
    public void AddFileFormatsRegistersEveryParserTheViewersTakeThroughAnInterface()
    {
        ServiceCollection services = new();
        services.AddFileFormats();
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        Type[] interfaces =
        [
            typeof(ICertificateReader), typeof(ICsvDocumentParser), typeof(IHarDocumentParser), typeof(IJsonValidator),
            typeof(IXmlDocumentParser), typeof(IXmlValidator), typeof(IXpsDocumentReader), typeof(IMarkdownParser),
        ];
        foreach (Type interfaceType in interfaces)
        {
            Assert.IsNotNull(provider.GetService(interfaceType), $"{interfaceType.Name} is not registered");
        }
    }
}
