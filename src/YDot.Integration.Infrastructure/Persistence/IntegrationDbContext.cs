using Microsoft.EntityFrameworkCore;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Domain.Entities;

namespace YDot.Integration.Infrastructure.Persistence;

/// <summary>
/// The EF Core context for the int_* tables.
///
/// IT SHARES THE ydotphaseupdated DATABASE WITH IAM, CAM, DON AND PAY, the same way those four
/// share it with each other: its own table prefix (int_) and its OWN migrations-history table
/// (__ef_migrations_history_int), so it migrates independently and never sees - or is seen by -
/// the other services' migrations. Column names are snake_case, like the rest of the platform.
///
/// The YDot CAM API reads these tables (read-only, plus inserts into int_worker_commands) to serve
/// the /app/whatsapp/check-number and /app/whatsapp/wati screens.
/// </summary>
public sealed class IntegrationDbContext(DbContextOptions<IntegrationDbContext> options)
    : DbContext(options), IIntegrationDbContext
{
    public const string MigrationsHistoryTable = "__ef_migrations_history_int";

    public DbSet<InputFile> InputFiles => Set<InputFile>();

    public DbSet<InputFileNumber> InputFileNumbers => Set<InputFileNumber>();

    public DbSet<IntegrationJob> IntegrationJobs => Set<IntegrationJob>();

    public DbSet<PhoneNumber> PhoneNumbers => Set<PhoneNumber>();

    public DbSet<CheckNumberTask> CheckNumberTasks => Set<CheckNumberTask>();

    public DbSet<CheckNumberTaskPoll> CheckNumberTaskPolls => Set<CheckNumberTaskPoll>();

    public DbSet<CheckNumberResultFile> CheckNumberResultFiles => Set<CheckNumberResultFile>();

    public DbSet<CheckNumberResult> CheckNumberResults => Set<CheckNumberResult>();

    public DbSet<WatiJob> WatiJobs => Set<WatiJob>();

    public DbSet<WatiBatch> WatiBatches => Set<WatiBatch>();

    public DbSet<WatiMessage> WatiMessages => Set<WatiMessage>();

    public DbSet<WatiTemplate> WatiTemplates => Set<WatiTemplate>();

    public DbSet<JobStepExecution> JobSteps => Set<JobStepExecution>();

    public DbSet<JobEvent> JobEvents => Set<JobEvent>();

    public DbSet<ApiCallLog> ApiCallLogs => Set<ApiCallLog>();

    public DbSet<WorkerInstance> WorkerInstances => Set<WorkerInstance>();

    public DbSet<WorkerCommand> WorkerCommands => Set<WorkerCommand>();

    public DbSet<JobSequence> JobSequences => Set<JobSequence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IntegrationDbContext).Assembly);
    }
}
