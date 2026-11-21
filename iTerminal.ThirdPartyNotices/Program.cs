// Writes THIRD-PARTY-NOTICES.txt for a publish output: every NuGet package whose files are part of the
// published app (for the runtime being published), its licence, its copyright line, the licence texts
// those licences name, and each licence or notice file the package or the runtime pack ships.
// Run after publish by iTerminal/Build/ThirdPartyNotices.targets; see Documentation/architecture/build-and-packaging.md.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ThirdPartyNotices;

public static class Program
{
    // Matches the file names that carry a licence or notice text inside a package folder.
    private static readonly Regex NoticeFileName = new(
        @"^(license|licence|notice|third[-_ ]?party|copying)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Licence tokens that name more than one text file.
    private static readonly Dictionary<string, string[]> TextsForToken = new(StringComparer.OrdinalIgnoreCase)
    {
        // LGPL-3.0 refers to the GPL-3.0 text for its terms, so both are needed.
        ["LGPL-3.0"] = ["LGPL-3.0", "GPL-3.0"],
        ["LGPL-3.0-or-later"] = ["LGPL-3.0", "GPL-3.0"],
    };

    private sealed class Package
    {
        public string Id = "";
        public string Version = "";
        public string Expression = "";     // SPDX expression, or "" when the nuspec names a file or nothing
        public string LicenceFile = "";    // bundled licence file named by the nuspec, when it is the licence
        public string LicenceUrl = "";
        public string Copyright = "";
        public string Authors = "";
        public string ProjectUrl = "";
        public List<int> NoticeIndexes = [];
    }

    private sealed class Notice
    {
        public string Label = "";
        public string Text = "";
        public List<string> UsedBy = [];
    }

    private sealed class Options
    {
        public string AssetsFile = "";
        public string TargetKey = "";
        public string PackagesRoot = "";
        public string LicenseTextsDir = "";
        public string OutputFile = "";
        public string ProductName = "iTerminal";
        public string ProductVersion = "";
        public List<string> RuntimePackDirectories = [];
    }

    private static int warnings;

    // Warnings do not fail the publish: a licence that cannot be read is shown in the notice file and in the build output.

    public static int Main(string[] args)
    {
        try
        {
            Options options = Parse(args);
            Run(options);
            return 0;
        }
        catch (UsageException exception)
        {
            Console.Error.WriteLine("error: " + exception.Message);
            Console.Error.WriteLine("usage: ThirdPartyNotices --assets <project.assets.json> --target <tfm[/rid]> --packages <nuget root> --licenses <dir> --output <file> [--runtime-pack <dir>]... [--product <name>] [--version <v>]");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("error: " + exception.Message);
            return 1;
        }
    }

    private sealed class UsageException(string message) : Exception(message);

    private static Options Parse(string[] args)
    {
        Options options = new();
        for (int i = 0; i < args.Length; i++)
        {
            string name = args[i];
            if (!name.StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                throw new UsageException($"unexpected argument '{name}'");
            }
            string value = args[++i];
            switch (name)
            {
                case "--assets": options.AssetsFile = value; break;
                case "--target": options.TargetKey = value; break;
                case "--packages": options.PackagesRoot = value; break;
                case "--licenses": options.LicenseTextsDir = value; break;
                case "--output": options.OutputFile = value; break;
                case "--runtime-pack": options.RuntimePackDirectories.Add(value); break;
                case "--product": options.ProductName = value; break;
                case "--version": options.ProductVersion = value; break;
                default: throw new UsageException($"unknown option '{name}'");
            }
        }

        foreach ((string label, string value) in new[]
        {
            ("--assets", options.AssetsFile), ("--target", options.TargetKey), ("--packages", options.PackagesRoot),
            ("--licenses", options.LicenseTextsDir), ("--output", options.OutputFile),
        })
        {
            if (value.Length == 0)
            {
                throw new UsageException($"{label} is required");
            }
        }
        return options;
    }

    private static void Run(Options options)
    {
        Dictionary<string, string> overrides = ReadOverrides(options.LicenseTextsDir);
        List<Package> packages = ReadShippedPackages(options, overrides);
        List<Notice> notices = [];
        Dictionary<string, int> noticeByHash = new();

        foreach (Package package in packages)
        {
            string folder = Path.Combine(options.PackagesRoot, package.Id.ToLowerInvariant(), package.Version);
            string label = $"{package.Id} {package.Version}";
            if (package.LicenceFile.Length > 0)
            {
                AddNotice(Path.Combine(folder, package.LicenceFile), label, package, notices, noticeByHash);
            }
            foreach (string file in NoticeFilesIn(folder))
            {
                AddNotice(file, label, package, notices, noticeByHash);
            }
        }

        foreach (string directory in options.RuntimePackDirectories)
        {
            string label = "runtime pack " + Path.GetFileName(directory.TrimEnd('/', '\\'));
            foreach (string file in NoticeFilesIn(directory))
            {
                AddNotice(file, label, null, notices, noticeByHash);
            }
        }

        // The licence texts the packages name, once each, in the order they are first named.
        List<string> licenceNames = [];
        foreach (Package package in packages)
        {
            foreach (string token in LicenceTokens(package.Expression))
            {
                foreach (string name in TextsFor(token))
                {
                    if (!licenceNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        licenceNames.Add(name);
                    }
                }
            }
        }

        string directoryName = Path.GetDirectoryName(Path.GetFullPath(options.OutputFile)) ?? ".";
        Directory.CreateDirectory(directoryName);
        File.WriteAllText(options.OutputFile, Compose(options, packages, notices, licenceNames), new UTF8Encoding(false));
        Console.WriteLine($"Third-party notices: {packages.Count} packages, {licenceNames.Count} licence texts, {notices.Count} notice files -> {options.OutputFile}");
    }

    private static void Warn(string message)
    {
        warnings++;
        Console.WriteLine("warning: " + message);
    }

    private static Dictionary<string, string> ReadOverrides(string licenseTextsDir)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(licenseTextsDir, "Overrides.txt");
        if (!File.Exists(path))
        {
            return result;
        }
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
            {
                Warn($"Overrides.txt: expected '<id> <version> <licence>', got '{line}'.");
                continue;
            }
            result[parts[0] + "/" + parts[1]] = parts[2];
        }
        return result;
    }

    private static List<Package> ReadShippedPackages(Options options, Dictionary<string, string> overrides)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(options.AssetsFile));
        JsonElement targets = document.RootElement.GetProperty("targets");
        if (!targets.TryGetProperty(options.TargetKey, out JsonElement section))
        {
            throw new InvalidOperationException($"'{options.TargetKey}' is not a target of {options.AssetsFile}; restore for that runtime first.");
        }

        List<Package> packages = [];
        foreach (JsonProperty entry in section.EnumerateObject())
        {
            // Only the files that are copied into the publish output count: a build-only or compile-only package is not shipped.
            if (!entry.Value.TryGetProperty("type", out JsonElement type) || type.GetString() != "package")
            {
                continue;
            }
            bool shipped = entry.Value.TryGetProperty("runtime", out _)
                || entry.Value.TryGetProperty("native", out _)
                || entry.Value.TryGetProperty("resource", out _);
            if (!shipped)
            {
                continue;
            }

            int slash = entry.Name.LastIndexOf('/');
            Package package = ReadNuspec(options.PackagesRoot, entry.Name[..slash], entry.Name[(slash + 1)..]);
            if (package.Expression.Length == 0 && package.LicenceFile.Length == 0)
            {
                if (overrides.TryGetValue(entry.Name, out string? licence))
                {
                    package.Expression = licence;
                }
                else
                {
                    Warn($"No licence statement for {entry.Name} (licence URL: {package.LicenceUrl}). Add it to iTerminal.ThirdPartyNotices/Licenses/Overrides.txt.");
                }
            }
            packages.Add(package);
        }
        return packages
            .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Version, StringComparer.Ordinal)
            .ToList();
    }

    private static Package ReadNuspec(string packagesRoot, string id, string version)
    {
        string folder = Path.Combine(packagesRoot, id.ToLowerInvariant(), version);
        string nuspec = Path.Combine(folder, id.ToLowerInvariant() + ".nuspec");
        if (!File.Exists(nuspec))
        {
            throw new FileNotFoundException($"The nuspec of {id} {version} is not in {folder}; restore the project first.", nuspec);
        }

        XElement metadata = XDocument.Load(nuspec).Root!.Elements().First(e => e.Name.LocalName == "metadata");
        XElement? license = Child(metadata, "license");
        Package package = new()
        {
            Id = id,
            Version = version,
            LicenceUrl = Child(metadata, "licenseUrl")?.Value.Trim() ?? "",
            Copyright = Child(metadata, "copyright")?.Value.Trim() ?? "",
            Authors = Child(metadata, "authors")?.Value.Trim() ?? "",
            ProjectUrl = Child(metadata, "projectUrl")?.Value.Trim() ?? "",
        };
        if (license is not null && string.Equals(license.Attribute("type")?.Value, "file", StringComparison.OrdinalIgnoreCase))
        {
            package.LicenceFile = license.Value.Trim();
        }
        else if (license is not null)
        {
            package.Expression = license.Value.Trim();
        }
        return package;
    }

    private static XElement? Child(XElement parent, string name) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == name);

    private static IEnumerable<string> NoticeFilesIn(string folder)
    {
        if (!Directory.Exists(folder))
        {
            yield break;
        }
        foreach (string file in Directory.EnumerateFiles(folder).OrderBy(f => f, StringComparer.Ordinal))
        {
            if (NoticeFileName.IsMatch(Path.GetFileName(file)))
            {
                yield return file;
            }
        }
    }

    private static void AddNotice(string file, string label, Package? package, List<Notice> notices, Dictionary<string, int> noticeByHash)
    {
        if (!File.Exists(file))
        {
            return;
        }
        byte[] bytes = File.ReadAllBytes(file);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!noticeByHash.TryGetValue(hash, out int index))
        {
            index = notices.Count + 1;
            noticeByHash[hash] = index;
            notices.Add(new Notice
            {
                Label = $"[{index}] {Path.GetFileName(file)}",
                Text = Normalise(Encoding.UTF8.GetString(bytes)),
            });
        }
        Notice notice = notices[index - 1];
        if (!notice.UsedBy.Contains(label))
        {
            notice.UsedBy.Add(label);
        }
        package?.NoticeIndexes.Add(index);
    }

    private static IEnumerable<string> LicenceTokens(string expression)
    {
        foreach (Match match in Regex.Matches(expression, @"[A-Za-z0-9][A-Za-z0-9.+\-]*"))
        {
            string token = match.Value;
            if (token is "OR" or "AND" or "WITH")
            {
                continue;
            }
            yield return token.TrimEnd('+');
        }
    }

    private static IEnumerable<string> TextsFor(string token)
    {
        if (TextsForToken.TryGetValue(token, out string[]? names))
        {
            return names;
        }
        // "GPL-3.0-or-later" names the same text as "GPL-3.0": the suffix is not a separate file.
        return [token.Replace("-or-later", "", StringComparison.OrdinalIgnoreCase)];
    }

    private static string Compose(Options options, List<Package> packages, List<Notice> notices, List<string> licenceNames)
    {
        // Built with LF, converted to the platform's line ending once at the end.
        const string nl = "\n";
        StringBuilder text = new();
        text.Append($"Third-party notices for {options.ProductName} {options.ProductVersion}".TrimEnd()).Append(nl);
        text.Append(new string('=', 72)).Append(nl);
        text.Append($"{options.ProductName} itself is licensed under the MIT licence (LICENSE.txt in this folder)." + nl);
        text.Append("The packages listed below are shipped with it. Each one is used under the licence stated for it." + nl);
        text.Append("Section 1 lists the packages, section 2 the licence texts they name, and section 3 the notice" + nl);
        text.Append("and licence files the packages and the .NET runtime ship with their own text." + nl + nl);

        text.Append($"1. Packages ({packages.Count})" + nl);
        text.Append(new string('-', 72)).Append(nl);
        foreach (Package package in packages)
        {
            text.Append($"{package.Id} {package.Version}").Append(nl);
            text.Append("  Licence: ").Append(LicenceText(package)).Append(nl);
            string owner = package.Copyright.Length > 0
                ? package.Copyright
                : package.Authors.Length > 0 ? "Authors: " + package.Authors : "not stated";
            text.Append("  Copyright: ").Append(owner).Append(nl);
            if (package.ProjectUrl.Length > 0)
            {
                text.Append("  Project: ").Append(package.ProjectUrl).Append(nl);
            }
            if (package.NoticeIndexes.Count > 0)
            {
                text.Append("  Notice files: ").Append(string.Join(", ", package.NoticeIndexes.Distinct().Select(i => "[" + i + "]"))).Append(nl);
            }
        }
        text.Append(nl);

        text.Append($"2. Licence texts ({licenceNames.Count})" + nl);
        text.Append(new string('-', 72)).Append(nl);
        foreach (string name in licenceNames)
        {
            string path = Path.Combine(options.LicenseTextsDir, name + ".txt");
            text.Append("--- " + name + " ---").Append(nl);
            if (File.Exists(path))
            {
                text.Append(Normalise(File.ReadAllText(path))).Append(nl);
            }
            else
            {
                Warn($"No licence text for {name}: {path} is missing.");
                text.Append($"The text is not included; see https://spdx.org/licenses/{name}.html" + nl + nl);
            }
        }

        text.Append($"3. Notice and licence files shipped with the packages and the runtime ({notices.Count})" + nl);
        text.Append(new string('-', 72)).Append(nl);
        foreach (Notice notice in notices)
        {
            text.Append("--- " + notice.Label + " ---").Append(nl);
            text.Append("Shipped by: " + string.Join(", ", notice.UsedBy) + nl + nl);
            text.Append(notice.Text).Append(nl).Append(nl);
        }
        return text.ToString().Replace("\n", Environment.NewLine);
    }

    private static string LicenceText(Package package)
    {
        if (package.Expression.Length > 0)
        {
            return package.Expression;
        }
        if (package.LicenceFile.Length > 0)
        {
            return "see " + package.LicenceFile + " in section 3";
        }
        return package.LicenceUrl.Length > 0 ? "not stated in the package; " + package.LicenceUrl : "not stated";
    }

    private static string Normalise(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n', ' ', '\t') + "\n";
}
