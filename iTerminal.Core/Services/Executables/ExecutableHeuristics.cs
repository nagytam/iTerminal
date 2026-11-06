using iConsole.Executables;

namespace iTerminal.Core.Services.Executables;

/// <summary>The packer and high-entropy hints, the same for every format.</summary>
internal static class ExecutableHeuristics
{
    /// <summary>Bits per byte above which a section's bytes look compressed or encrypted. Plain code is near 6, text near 5, a zip near 7.99.</summary>
    public const double HighEntropy = 7.2;

    /// <summary>A section smaller than this says too little for its entropy to mean anything.</summary>
    public const long MinimumSectionSize = 256;

    /// <summary>Section names that a packer or protector leaves behind. Compared without case.</summary>
    private static readonly HashSet<string> PackerSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "UPX0", "UPX1", "UPX2", "UPX!", ".upx", ".aspack", ".adata", "ASPack", ".themida", ".winlice", ".vmp0", ".vmp1", ".vmp2",
        ".petite", ".nsp0", ".nsp1", ".nsp2", ".MPRESS1", ".MPRESS2", ".packed", ".enigma1", ".enigma2", ".boom", "FSG!", ".yP", ".Upack",
        ".perplex", ".pec1", ".pec2", "PEC2TO", "PECompact2", ".shrink1", ".shrink2", ".RLPack", ".Armadillo",
    };

    /// <summary>Adds the hints that follow from the sections.</summary>
    public static void AddSectionHints(IReadOnlyList<ExecutableSection> sections, List<ExecutableHint> hints)
    {
        foreach (ExecutableSection section in sections)
        {
            if (PackerSections.Contains(section.Name))
            {
                hints.Add(new ExecutableHint(ExecutableHintKind.PackerSectionName, section.Name));
            }

            if (section.Entropy is >= HighEntropy && section.FileSize >= MinimumSectionSize)
            {
                hints.Add(new ExecutableHint(ExecutableHintKind.HighEntropySection, section.Name));
            }

            if (section.Executable && section.Writable)
            {
                hints.Add(new ExecutableHint(ExecutableHintKind.WritableExecutableSection, section.Name));
            }

            if (section.Sampled)
            {
                hints.Add(new ExecutableHint(ExecutableHintKind.EntropySampled, section.Name));
            }
        }
    }
}
