using System.Resources;

namespace iConsole.Localization;

/// <summary>
/// A set of <c>.resx</c> resources the localizer looks keys up in. The framework registers its own;
/// the app registers another, so each assembly keeps the strings of the controls it owns.
/// </summary>
public interface ILocalizationSource
{
    ResourceManager Resources { get; }
}

/// <summary>An <see cref="ILocalizationSource"/> over one <see cref="ResourceManager"/>.</summary>
public sealed class ResourceLocalizationSource(ResourceManager resources) : ILocalizationSource
{
    public ResourceLocalizationSource() : this(new ResourceManager(typeof(ResourceLocalizationSource)))
    {
    }

    public ResourceManager Resources { get; } = resources;
}
