using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDot.Integration.Domain.Entities;

namespace YDot.Integration.Infrastructure.Persistence.Configurations;

/// <summary>int_wati_jobs: one WATI send run (automatic or manual).</summary>
internal sealed class WatiJobConfiguration : IEntityTypeConfiguration<WatiJob>
{
    public void Configure(EntityTypeBuilder<WatiJob> builder)
    {
        builder.ToTable("int_wati_jobs", t => t.HasComment("WATI send runs: template, broadcast, parameters, recipients and outcome counts."));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Reference).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Source).AsText(20);
        builder.Property(x => x.Status).AsText(30);
        builder.Property(x => x.TemplateName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TemplateLabel).HasMaxLength(200);
        builder.Property(x => x.BroadcastName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ParametersJson).AsJson().IsRequired();
        builder.Property(x => x.RenderedPreview).HasMaxLength(4000);
        builder.Property(x => x.ChannelNumber).HasMaxLength(20);
        builder.Property(x => x.ErrorCode).HasMaxLength(60);
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);
        builder.Property(x => x.RequestedByName).HasMaxLength(200);
        builder.Property(x => x.LockedBy).HasMaxLength(200);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Ignore(x => x.IsTerminal);

        builder.HasIndex(x => x.Reference).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
        builder.HasIndex(x => x.IntegrationJobId);
        builder.HasIndex(x => x.CampaignId);

        builder.HasMany(x => x.Batches).WithOne().HasForeignKey(b => b.WatiJobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Messages).WithOne(m => m.WatiJob).HasForeignKey(m => m.WatiJobId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>int_wati_batches: one sendTemplateMessages call.</summary>
internal sealed class WatiBatchConfiguration : IEntityTypeConfiguration<WatiBatch>
{
    public void Configure(EntityTypeBuilder<WatiBatch> builder)
    {
        builder.ToTable("int_wati_batches", t => t.HasComment("Each sendTemplateMessages HTTP call: request, response, HTTP status, attempts and timing."));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).AsText(30);
        builder.Property(x => x.RequestPayloadJson).AsJson();
        builder.Property(x => x.ResponseBody).HasMaxLength(8000);
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);

        builder.HasIndex(x => new { x.WatiJobId, x.BatchNumber }).IsUnique();
        builder.HasIndex(x => x.Status);
    }
}

/// <summary>int_wati_messages: one message per number, sent or skipped.</summary>
internal sealed class WatiMessageConfiguration : IEntityTypeConfiguration<WatiMessage>
{
    public void Configure(EntityTypeBuilder<WatiMessage> builder)
    {
        builder.ToTable("int_wati_messages", t => t.HasComment("One WhatsApp template message per number (or the reason it was skipped), followed to sent / delivered / read."));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CheckNumberTaskId).HasColumnName("checknumber_task_id");
        builder.Property(x => x.CheckNumberProviderTaskId).HasMaxLength(64).HasColumnName("checknumber_provider_task_id");
        builder.Property(x => x.WhatsAppNumber).HasMaxLength(20).IsRequired().HasColumnName("whatsapp_number");
        builder.Property(x => x.WhatsAppCheck).AsText(10).HasColumnName("whatsapp_check");
        builder.Property(x => x.IsValidWhatsAppNumber).HasColumnName("is_valid_whatsapp_number");
        builder.Property(x => x.TemplateName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.BroadcastName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ParametersJson).AsJson().IsRequired();
        builder.Property(x => x.Status).AsText(20);
        builder.Property(x => x.SkipReason).HasMaxLength(40);
        builder.Property(x => x.RequestStatus).AsText(20);
        builder.Property(x => x.WatiResponseJson).AsJson();
        builder.Property(x => x.LocalMessageId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.WatiMessageId).HasMaxLength(200);
        builder.Property(x => x.WhatsAppMessageId).HasMaxLength(200).HasColumnName("whatsapp_message_id");
        builder.Property(x => x.ConversationId).HasMaxLength(200);
        builder.Property(x => x.DeliveryStatusRaw).HasMaxLength(40);
        builder.Property(x => x.FailedCode).HasMaxLength(60);
        builder.Property(x => x.FailedReason).HasMaxLength(2000);
        builder.Property(x => x.ReplyText).HasMaxLength(4000);
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);
        builder.Ignore(x => x.IsDeliveryFinal);

        // A number gets at most one message per WATI job, and every localMessageId is unique.
        builder.HasIndex(x => new { x.WatiJobId, x.PhoneNumberId }).IsUnique();
        builder.HasIndex(x => x.LocalMessageId).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => new { x.Status, x.NextStatusCheckAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.PhoneNumberId, x.TemplateName });
        builder.HasIndex(x => x.IntegrationJobId);
        builder.HasIndex(x => x.CampaignId);

        builder.HasOne(x => x.WatiBatch).WithMany().HasForeignKey(x => x.WatiBatchId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.PhoneNumber).WithMany().HasForeignKey(x => x.PhoneNumberId).OnDelete(DeleteBehavior.NoAction);
    }
}

/// <summary>int_wati_templates: the templates offered on the Wati page.</summary>
internal sealed class WatiTemplateConfiguration : IEntityTypeConfiguration<WatiTemplate>
{
    public void Configure(EntityTypeBuilder<WatiTemplate> builder)
    {
        builder.ToTable("int_wati_templates", t => t.HasComment("WhatsApp templates copied from WATI (or declared in configuration) for the Wati page."));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ElementName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DisplayLabel).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(40);
        builder.Property(x => x.Status).HasMaxLength(40);
        builder.Property(x => x.Language).HasMaxLength(20);
        builder.Property(x => x.Body).HasMaxLength(4000);
        builder.Property(x => x.HeaderJson).AsJson();
        builder.Property(x => x.Footer).HasMaxLength(1000);
        builder.Property(x => x.ButtonsJson).AsJson();
        builder.Property(x => x.ParametersJson).AsJson().IsRequired();
        builder.Property(x => x.Source).AsText(20);

        builder.HasIndex(x => x.ElementName).IsUnique();
    }
}
