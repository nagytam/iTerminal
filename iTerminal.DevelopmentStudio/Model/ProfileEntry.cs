namespace iTerminal.DevelopmentStudio.Model;

/// <summary>One function of a profile: how often it was called, the time spent in it, and the time including its callees.</summary>
public sealed record ProfileEntry(long Calls, double TotalSeconds, double CumulativeSeconds, string Function);
