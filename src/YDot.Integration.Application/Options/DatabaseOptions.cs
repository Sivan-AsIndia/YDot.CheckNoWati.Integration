namespace YDot.Integration.Application.Options;

/// <summary>
/// Database behaviour. Section <c>Database</c>. The connection string itself is
/// <c>ConnectionStrings:IntegrationDatabase</c> (set ConnectionStrings__IntegrationDatabase in .env).
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public const string ConnectionStringName = "IntegrationDatabase";

    /// <summary>Create / upgrade the int_* tables on start-up.</summary>
    public bool ApplyMigrationsOnStartup { get; set; } = true;

    public int CommandTimeoutSeconds { get; set; } = 60;

    /// <summary>How long start-up keeps trying to reach the database (e.g. while PostgreSQL boots).</summary>
    public int StartupConnectTimeoutSeconds { get; set; } = 120;
}
