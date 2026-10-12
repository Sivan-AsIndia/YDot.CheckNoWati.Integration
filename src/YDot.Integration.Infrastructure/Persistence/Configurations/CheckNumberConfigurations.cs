using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDot.Integration.Domain.Entities;

namespace YDot.Integration.Infrastructure.Persistence.Configurations;

/// <summary>int_checknumber_tasks: one row per CheckNumber.ai task, written before the create call.</summary>
internal sealed class CheckNumberTaskConfiguration : IEntityTypeConfiguration<CheckNumberTask>
{
    public void Configure(EntityTypeBuilder<CheckNumberTask> builder)
    {
        builder.ToTable("int_checknumber_tasks", t => t.HasComment("CheckNumber.ai batch tasks: task_id, status, counts, cost, result_url and polling state."));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProviderTaskId).HasMaxLength(64);
        builder.Property(x => x.TaskType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.State).AsText(20);
        builder.Property(x => x.ProviderStatus).HasMaxLength(40);
        builder.Property(x => x.CountryCode).HasMaxLength(4);
        builder.Property(x => x.EstimatedAmount).AsMoney();
        builder.Property(x => x.ActualAmount).AsMoney();
        builder.Property(x => x.Currency).HasMaxLength(10);
        builder.Property(x => x.ResultUrl).HasMaxLength(4000);
        builder.Property(x => x.ProviderMessage).HasMaxLength(2000);
        builder.Property(x => x.SubmittedNumbersPath).HasMaxLength(1000);
        builder.Property(x => x.ErrorCode).HasMaxLength(60);
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);

        builder.HasIndex(x => x.ProviderTaskId).IsUnique().HasFilter("provider_task_id IS NOT NULL");
        builder.HasIndex(x => new { x.JobId, x.Sequence }).IsUnique().HasFilter("is_superseded = false");
        builder.HasIndex(x => new { x.State, x.NextPollAtUtc });

        builder.HasMany(x => x.Polls).WithOne().HasForeignKey(p => p.CheckNumberTaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ResultFile).WithOne().HasForeignKey<CheckNumberResultFile>(r => r.CheckNumberTaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Results).WithOne().HasForeignKey(r => r.CheckNumberTaskId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>int_checknumber_task_polls: each status check of a task.</summary>
internal sealed class CheckNumberTaskPollConfiguration : IEntityTypeConfiguration<CheckNumberTaskPoll>
{
    public void Configure(EntityTypeBuilder<CheckNumberTaskPoll> builder)
    {
        builder.ToTable("int_checknumber_task_polls", t => t.HasComment("Every gettasks status check of a CheckNumber.ai task, with the counts it reported."));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CheckNumberTaskId).HasColumnName("checknumber_task_id");
        builder.Property(x => x.ProviderStatus).HasMaxLength(40);
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);

        builder.HasIndex(x => new { x.CheckNumberTaskId, x.PollNumber });
        builder.HasIndex(x => x.JobId);
    }
}

/// <summary>int_checknumber_result_files: the downloaded archive and its extraction.</summary>
internal sealed class CheckNumberResultFileConfiguration : IEntityTypeConfiguration<CheckNumberResultFile>
{
    public void Configure(EntityTypeBuilder<CheckNumberResultFile> builder)
    {
        builder.ToTable("int_checknumber_result_files", t => t.HasComment("Download (HTTP status, size, duration, path) and extraction (entries, errors) of a task's result archive."));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CheckNumberTaskId).HasColumnName("checknumber_task_id");
        builder.Property(x => x.ProviderTaskId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ResultUrl).HasMaxLength(4000);
        builder.Property(x => x.DownloadStatus).AsText(20);
        builder.Property(x => x.FileSha256).HasMaxLength(64);
        builder.Property(x => x.LocalZipPath).HasMaxLength(1000);
        builder.Property(x => x.DownloadError).HasMaxLength(2000);
        builder.Property(x => x.ExtractionStatus).AsText(20);
        builder.Property(x => x.ExtractedPath).HasMaxLength(1000);
        builder.Property(x => x.ExtractedEntriesJson).AsJson();
        builder.Property(x => x.ExtractionError).HasMaxLength(2000);
        builder.Property(x => x.ResultSource).HasMaxLength(60);

        builder.HasIndex(x => x.CheckNumberTaskId).IsUnique();
        builder.HasIndex(x => x.JobId);
    }
}

/// <summary>int_checknumber_results: CheckNumber.ai's answer per submitted number.</summary>
internal sealed class CheckNumberResultConfiguration : IEntityTypeConfiguration<CheckNumberResult>
{
    public void Configure(EntityTypeBuilder<CheckNumberResult> builder)
    {
        builder.ToTable("int_checknumber_results", t => t.HasComment("CheckNumber.ai answer for each submitted number: returned number, activated value, WhatsApp yes/no/unknown and why."));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SubmittedNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ReturnedNumber).HasMaxLength(24);
        builder.Property(x => x.ActivatedRaw).HasMaxLength(20);
        builder.Property(x => x.CheckNumberTaskId).HasColumnName("checknumber_task_id");
        builder.Property(x => x.WhatsAppStatus).AsText(10).HasColumnName("whatsapp_status");
        builder.Property(x => x.ValidationStatus).AsText(20);
        builder.Property(x => x.Note).HasMaxLength(1000);

        builder.HasIndex(x => new { x.CheckNumberTaskId, x.PhoneNumberId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.PhoneNumberId, x.CheckedAtUtc });
        builder.HasIndex(x => x.JobId);

        builder.HasOne(x => x.PhoneNumber).WithMany().HasForeignKey(x => x.PhoneNumberId).OnDelete(DeleteBehavior.NoAction);
    }
}
