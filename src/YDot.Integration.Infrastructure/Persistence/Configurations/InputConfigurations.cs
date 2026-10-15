using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDot.Integration.Domain.Entities;

namespace YDot.Integration.Infrastructure.Persistence.Configurations;

/// <summary>int_input_files: one row per file that reached the inbox.</summary>
internal sealed class InputFileConfiguration : IEntityTypeConfiguration<InputFile>
{
    public void Configure(EntityTypeBuilder<InputFile> builder)
    {
        builder.ToTable("int_input_files", t => t.HasComment("Every mobile-number file that reached the worker inbox, including duplicates and rejected files."));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantCode).HasMaxLength(63);
        builder.Property(x => x.TenantName).HasMaxLength(200);
        builder.Property(x => x.UploadReference).HasMaxLength(40);
        builder.Property(x => x.Source).AsText();
        builder.Property(x => x.Status).AsText();
        builder.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.FileKind).AsText(10);
        builder.Property(x => x.ContentSha256).HasMaxLength(64).IsRequired();
        builder.Property(x => x.DedupeKey).HasMaxLength(200);
        builder.Property(x => x.StoredFilePath).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ManifestJson).AsJson();
        builder.Property(x => x.CampaignCode).HasMaxLength(60);
        builder.Property(x => x.CampaignName).HasMaxLength(250);
        builder.Property(x => x.UploadedByName).HasMaxLength(200);
        builder.Property(x => x.RejectionReason).HasMaxLength(2000);

        // THE DUPLICATE GUARD: organisation + content hash + campaign can be registered once.
        builder.HasIndex(x => x.DedupeKey).IsUnique().HasFilter("dedupe_key IS NOT NULL");
        builder.HasIndex(x => x.UploadReference).IsUnique().HasFilter("upload_reference IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.ReceivedAtUtc });
        builder.HasIndex(x => x.ContentSha256);
        builder.HasIndex(x => x.CampaignId);

        builder.HasOne(x => x.DuplicateOf)
            .WithMany()
            .HasForeignKey(x => x.DuplicateOfInputFileId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

/// <summary>int_integration_jobs: one pipeline run per registered input file.</summary>
internal sealed class IntegrationJobConfiguration : IEntityTypeConfiguration<IntegrationJob>
{
    public void Configure(EntityTypeBuilder<IntegrationJob> builder)
    {
        builder.ToTable("int_integration_jobs", t => t.HasComment("One pipeline run (parse -> CheckNumber.ai -> verify -> WATI -> output) per input file."));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.JobNumber).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Status).AsText();
        builder.Property(x => x.CurrentStep).HasMaxLength(60);
        builder.Property(x => x.RunCheckNumber).HasColumnName("run_checknumber");
        builder.Property(x => x.WatiTemplateName).HasMaxLength(200);
        builder.Property(x => x.WatiBroadcastName).HasMaxLength(200);
        builder.Property(x => x.WatiParametersJson).AsJson();
        builder.Property(x => x.WhatsAppYes).HasColumnName("whatsapp_yes");
        builder.Property(x => x.WhatsAppNo).HasColumnName("whatsapp_no");
        builder.Property(x => x.WhatsAppUnknown).HasColumnName("whatsapp_unknown");
        builder.Property(x => x.EstimatedCost).AsMoney();
        builder.Property(x => x.ActualCost).AsMoney();
        builder.Property(x => x.CostCurrency).HasMaxLength(10);
        builder.Property(x => x.OutcomeMessage).HasMaxLength(2000);
        builder.Property(x => x.ErrorCode).HasMaxLength(60);
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);
        builder.Property(x => x.LockedBy).HasMaxLength(200);
        builder.Property(x => x.CreatedByName).HasMaxLength(200);
        builder.Property(x => x.WorkingDirectory).HasMaxLength(1000);
        builder.Property(x => x.ResultsCsvPath).HasMaxLength(1000);
        builder.Property(x => x.SummaryJsonPath).HasMaxLength(1000);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Ignore(x => x.IsTerminal);

        builder.HasIndex(x => x.JobNumber).IsUnique();
        builder.HasIndex(x => x.InputFileId).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
        builder.HasIndex(x => x.CampaignId);

        builder.HasOne(x => x.InputFile)
            .WithOne(f => f.Job)
            .HasForeignKey<IntegrationJob>(x => x.InputFileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Numbers).WithOne().HasForeignKey(n => n.JobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.CheckNumberTasks).WithOne(t => t.Job).HasForeignKey(t => t.JobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Steps).WithOne().HasForeignKey(s => s.JobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.WatiJobs).WithOne(w => w.IntegrationJob).HasForeignKey(w => w.IntegrationJobId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>int_input_file_numbers: every number (and every unusable line) found in a file.</summary>
internal sealed class InputFileNumberConfiguration : IEntityTypeConfiguration<InputFileNumber>
{
    public void Configure(EntityTypeBuilder<InputFileNumber> builder)
    {
        builder.ToTable("int_input_file_numbers", t => t.HasComment("Every occurrence found in an input file: original text, normalised number, classification and the answer the job used."));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SheetName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RawValue).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ContactName).HasMaxLength(120);
        builder.Property(x => x.NormalizedNumber).HasMaxLength(20);
        builder.Property(x => x.CountryCode).HasMaxLength(4);
        builder.Property(x => x.CountryIso).HasMaxLength(4);
        builder.Property(x => x.CountryName).HasMaxLength(60);
        builder.Property(x => x.Classification).AsText(20);
        builder.Property(x => x.InvalidReason).HasMaxLength(300);
        builder.Property(x => x.ReusedWhatsAppStatus).AsText(10).HasColumnName("reused_whatsapp_status");
        builder.Property(x => x.FinalWhatsAppStatus).AsText(10).HasColumnName("final_whatsapp_status");
        builder.Property(x => x.CheckNumberTaskId).HasColumnName("checknumber_task_id");
        builder.Property(x => x.ResultNote).HasMaxLength(1000);

        builder.HasIndex(x => new { x.JobId, x.Sequence });
        builder.HasIndex(x => new { x.JobId, x.Classification });
        builder.HasIndex(x => new { x.TenantId, x.NormalizedNumber });
        builder.HasIndex(x => x.InputFileId);
        builder.HasIndex(x => x.CheckNumberTaskId);
        builder.HasIndex(x => x.PhoneNumberId);

        // THE SELECTION POOL: "this organisation's numbers that a check found on WhatsApp", which the
        // WhatsApp Messages page filters, pages and picks from. Partial, so it holds only the Yes rows.
        builder.HasIndex(x => new { x.TenantId, x.PhoneNumberId, x.CreatedAtUtc })
            .HasDatabaseName("ix_int_input_file_numbers_verified_pool")
            .HasFilter("final_whatsapp_status = 'Yes'");

        builder.HasOne<InputFile>().WithMany().HasForeignKey(x => x.InputFileId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.PhoneNumber).WithMany().HasForeignKey(x => x.PhoneNumberId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CheckNumberTask>().WithMany().HasForeignKey(x => x.CheckNumberTaskId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<InputFileNumber>().WithMany().HasForeignKey(x => x.DuplicateOfId).OnDelete(DeleteBehavior.NoAction);

        // ReusedFromResultId and CheckResultId are deliberate SOFT links (no foreign key): a result
        // file processed again after a restart replaces its result rows, and a pointer kept for
        // audit must not block that.
    }
}

/// <summary>int_phone_numbers: the registry that makes "already checked" possible.</summary>
internal sealed class PhoneNumberConfiguration : IEntityTypeConfiguration<PhoneNumber>
{
    public void Configure(EntityTypeBuilder<PhoneNumber> builder)
    {
        builder.ToTable("int_phone_numbers", t => t.HasComment("One row per distinct E.164 number with its latest definitive WhatsApp answer (the reuse cache)."));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.E164).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Digits).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CountryCode).HasMaxLength(4).IsRequired();
        builder.Property(x => x.RegionCode).HasMaxLength(4);
        builder.Property(x => x.CountryIso).HasMaxLength(4);
        builder.Property(x => x.CountryName).HasMaxLength(60);
        builder.Property(x => x.WhatsAppStatus).AsText(10).HasColumnName("whatsapp_status");
        builder.Property(x => x.WhatsAppCheckedAtUtc).HasColumnName("whatsapp_checked_at_utc");

        builder.HasIndex(x => x.E164).IsUnique();
        builder.HasIndex(x => x.Digits);
        builder.HasIndex(x => x.WhatsAppStatus);
    }
}
