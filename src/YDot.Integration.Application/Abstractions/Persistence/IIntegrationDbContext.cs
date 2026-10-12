using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using YDot.Integration.Domain.Entities;

namespace YDot.Integration.Application.Abstractions.Persistence;

/// <summary>
/// The worker's view of the int_* tables. Implemented by the EF Core context in Infrastructure;
/// the pipeline code queries through these sets and never sees the PostgreSQL provider.
/// </summary>
public interface IIntegrationDbContext
{
    DbSet<InputFile> InputFiles { get; }

    DbSet<InputFileNumber> InputFileNumbers { get; }

    DbSet<IntegrationJob> IntegrationJobs { get; }

    DbSet<PhoneNumber> PhoneNumbers { get; }

    DbSet<CheckNumberTask> CheckNumberTasks { get; }

    DbSet<CheckNumberTaskPoll> CheckNumberTaskPolls { get; }

    DbSet<CheckNumberResultFile> CheckNumberResultFiles { get; }

    DbSet<CheckNumberResult> CheckNumberResults { get; }

    DbSet<WatiJob> WatiJobs { get; }

    DbSet<WatiBatch> WatiBatches { get; }

    DbSet<WatiMessage> WatiMessages { get; }

    DbSet<WatiTemplate> WatiTemplates { get; }

    DbSet<JobStepExecution> JobSteps { get; }

    DbSet<JobEvent> JobEvents { get; }

    DbSet<ApiCallLog> ApiCallLogs { get; }

    DbSet<WorkerInstance> WorkerInstances { get; }

    DbSet<WorkerCommand> WorkerCommands { get; }

    DatabaseFacade Database { get; }

    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
