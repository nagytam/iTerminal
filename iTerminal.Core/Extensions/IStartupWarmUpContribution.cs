namespace iTerminal.Core.Extensions;

/// <summary>
/// A service that a feature outside iTerminal.Core asks the start-up to build on its own thread, as the slowest singletons
/// are built. The service must be registered with the container; the contribution only names its type.
/// </summary>
public interface IStartupWarmUpContribution
{
    Type ServiceType { get; }
}
