using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDot.Integration.Domain.Entities;

namespace YDot.Integration.Infrastructure.Persistence.Configurations;

/// <summary>int_job_steps: the job timeline.</summary>
internal sealed class JobStepExecutionConfiguration : IEntityTypeConfiguration<JobStepExecution>
{
    public void Configure(EntityTypeBuilder<JobStepExecution> builder)
    {
        builder.ToTable("int_job_steps", t => t.HasComment("Each execution of a pipeline step for a job: when, how long, outcome and error."));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.StepCode).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Status).AsText(20);
        builder.Property(x => x.Message).HasMaxLength(2000);
        builder.Property(x => x.ErrorCode).HasMaxLength(60);
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);
        builder.Property(x => x.DetailsJson).AsJson();
        builder.Property(x => x.WorkerInstance).HasMaxLength(200);

        builder.HasIndex(x => new { x.JobId, x.StartedAtUtc });
        builder.HasIndex(x => new { x.JobId, x.StepCode });
    }
}

/// <summary>int_job_events: the narrative of what happened, append-only.</summary>
internal sealed class JobEventConfiguration : IEntityTypeConfiguration<JobEvent>
{
    public void Configure(EntityTypeBuilder<JobEvent> builder)
    {
        builder.ToTable("int_job_events", t => t.HasComment("Append-only narrative: file registered, numbers reused, task created, retries, batches, completion."));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.StepCode).HasMaxLength(60);
        builder.Property(x => x.Level).AsText(20);
        builder.Property(x => x.EventCode).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.DataJson).AsJson();
        builder.Property(x => x.WorkerInstance).HasMaxLength(200);

        builder.HasIndex(x => new { x.JobId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.InputFileId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.WatiJobId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.OccurredAtUtc });
        builder.HasIndex(x => x.EventCode);

        builder.HasOne<IntegrationJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<InputFile>().WithMany().HasForeignKey(x => x.InputFileId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<WatiJob>().WithMany().HasForeignKey(x => x.WatiJobId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>int_api_call_logs: every HTTP attempt to an external system.</summary>
internal sealed class ApiCallLogConfiguration : IEntityTypeConfiguration<ApiCallLog>
{
    public void Configure(EntityTypeBuilder<ApiCallLog> builder)
    {
        builder.ToTable("int_api_call_logs", t => t.HasComment("One row per HTTP attempt to CheckNumber.ai, its result storage or WATI; secrets are never stored."));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Provider).AsText(30);
        builder.Property(x => x.Operation).HasMaxLength(60).IsRequired();
        builder.Property(x => x.HttpMethod).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Url).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.RequestBody).HasMaxLength(8000);
        builder.Property(x => x.ResponseBody).HasMaxLength(8000);
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);

        builder.HasIndex(x => x.StartedAtUtc);
        builder.HasIndex(x => x.JobId);
        builder.HasIndex(x => x.WatiJobId);
        builder.HasIndex(x => new { x.Provider, x.Operation, x.StartedAtUtc });
    }
}
