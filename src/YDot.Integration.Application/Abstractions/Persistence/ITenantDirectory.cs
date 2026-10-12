namespace YDot.Integration.Application.Abstractions.Persistence;

/// <summary>An organisation as IAM stores it.</summary>
public sealed record TenantInfo(Guid Id, string Code, string Name, string Subdomain, string Status);

/// <summary>
/// Read-only lookups of organisations in IAM's iam_tenants table (same database). Used to attach a
/// file copied into inbox/{subdomain}/ to its organisation, and to label jobs in logs. The worker
/// never writes IAM tables.
/// </summary>
public interface ITenantDirectory
{
    /// <summary>By subdomain (smilefoundation) or code (TEN001), case-insensitive.</summary>
    Task<TenantInfo?> FindAsync(string subdomainOrCode, CancellationToken cancellationToken);

    Task<TenantInfo?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken);
}
