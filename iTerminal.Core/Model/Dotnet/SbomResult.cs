namespace iTerminal.Core.Model.Dotnet;

/// <summary>The serialisation of a CycloneDX SBOM file.</summary>
public enum SbomFormat
{
    /// <summary>CycloneDX XML, <c>.cdx.xml</c>. The default.</summary>
    Xml,

    /// <summary>CycloneDX JSON, <c>.cdx.json</c>.</summary>
    Json,
}

/// <summary>One NuGet package in a solution's software bill of materials.</summary>
/// <param name="Name">Package id, as NuGet spells it.</param>
/// <param name="Version">Resolved package version.</param>
/// <param name="Purl">Package URL, <c>pkg:nuget/&lt;id&gt;@&lt;version&gt;</c>, with the id lower-cased.</param>
public sealed record SbomComponent(string Name, string Version, string Purl);

/// <summary>
/// Outcome of writing a solution's SBOM.
/// </summary>
/// <param name="Path">The file written. Empty when no project had an assets file, so nothing was written.</param>
/// <param name="ComponentCount">Distinct packages listed in the file.</param>
/// <param name="ProjectsWithoutAssets">Projects skipped because they have no readable <c>obj/project.assets.json</c>.</param>
public sealed record SbomResult(string Path, int ComponentCount, int ProjectsWithoutAssets);
