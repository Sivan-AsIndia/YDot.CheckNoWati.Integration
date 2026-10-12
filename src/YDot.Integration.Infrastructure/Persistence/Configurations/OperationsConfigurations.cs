using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDot.Integration.Domain.Entities;

namespace YDot.Integration.Infrastructure.Persistence.Configurations;

/// <summary>int_worker_instances: which workers are running.</summary>
internal sealed class WorkerInstanceConfiguration : IEntityTypeConfiguration<WorkerInstance>
{
    public void Configure(EntityTypeBuilder<WorkerInstance> builder)
    {
        builder.ToTable("int_worker_instances", t => t.HasComment("Running / stopped worker processes with heartbeat and effective non-secret settings."));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.InstanceName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.HostName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EnvironmentName).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.SettingsJson).AsJson();

        builder.HasIndex(x => x.LastHeartbeatAtUtc);
    }
}

/// <summary>int_worker_commands: requests from the web application.</summary>
internal sealed class WorkerCommandConfiguration : IEntityTypeConfiguration<WorkerCommand>
{
    public void Configure(EntityTypeBuilder<WorkerCommand> builder)
    {
        builder.ToTable("int_worker_commands", t => t.HasComment("Requests the YDot web application leaves for the worker: retry, cancel, manual broadcast, template refresh."));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Reference).HasMaxLength(30).IsRequired();
        builder.Property(x => x.CommandType).AsText(30);
        builder.Property(x => x.Status).AsText(20);
        builder.Property(x => x.PayloadJson).AsJson().IsRequired();
        builder.Property(x => x.ResultMessage).HasMaxLength(2000);
        builder.Property(x => x.RequestedByName).HasMaxLength(200);
        builder.Property(x => x.LockedBy).HasMaxLength(200);

        builder.HasIndex(x => x.Reference).IsUnique();
        builder.HasIndex(x => new { x.Status, x.RequestedAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.RequestedAtUtc });
    }
}

/// <summary>int_job_sequences: daily counters for INT-/WAT- references.</summary>
internal sealed class JobSequenceConfiguration : IEntityTypeConfiguration<JobSequence>
{
    public void Configure(EntityTypeBuilder<JobSequence> builder)
    {
        builder.ToTable("int_job_sequences", t => t.HasComment("Daily counters behind the human-readable job references."));
        builder.HasKey(x => new { x.Name, x.Period });

        builder.Property(x => x.Name).HasMaxLength(10);
        builder.Property(x => x.Period).HasMaxLength(8);
    }
}
