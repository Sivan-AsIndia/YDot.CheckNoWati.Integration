namespace YDot.Integration.Application.Abstractions.Persistence;

/// <summary>Hands out human-readable references such as INT-20261010-000001.</summary>
public interface IReferenceGenerator
{
    /// <summary>The next reference for <paramref name="prefix"/> today (UTC), e.g. "INT" -> INT-20261010-000007.</summary>
    Task<string> NextAsync(string prefix, CancellationToken cancellationToken);
}

/// <summary>The reference prefixes in use.</summary>
public static class ReferencePrefixes
{
    /// <summary>Integration jobs.</summary>
    public const string IntegrationJob = "INT";

    /// <summary>WATI jobs.</summary>
    public const string WatiJob = "WAT";
}
