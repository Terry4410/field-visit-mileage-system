using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FieldVisit.Infrastructure;

public sealed class NotificationModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        modelBuilder.Entity<Employment>().Property(x => x.OptionalEmailNotificationEnabled).HasDefaultValue(true);

        modelBuilder.Entity<NotificationEnvironmentPolicy>(e =>
        {
            e.ToTable("NotificationEnvironmentPolicies");
            e.HasKey(x => x.EnvironmentCode);
            e.Property(x => x.EnvironmentCode).HasMaxLength(20);
            e.Property(x => x.EmailMode).HasMaxLength(20);
            e.Property(x => x.SenderIdentityReference).HasMaxLength(200);
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<NotificationEmailAllowlist>(e =>
        {
            e.ToTable("NotificationEmailAllowlist");
            e.HasKey(x => x.NotificationEmailAllowlistId);
            e.Property(x => x.NotificationEmailAllowlistId).ValueGeneratedOnAdd();
            e.Property(x => x.EnvironmentCode).HasMaxLength(20);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.NormalizedEmail)
                .HasMaxLength(320)
                .HasComputedColumnSql("LOWER(LTRIM(RTRIM([Email])))", stored: true);
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.EnvironmentCode, x.NormalizedEmail })
                .IsUnique()
                .HasDatabaseName("UX_NotificationEmailAllowlist_Environment_Email");
            e.HasOne<NotificationEnvironmentPolicy>().WithMany().HasForeignKey(x => x.EnvironmentCode).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.InactivatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<NotificationSetting>(e =>
        {
            e.ToTable("NotificationSettings");
            e.HasKey(x => x.NotificationSettingId);
            e.Property(x => x.NotificationSettingId).ValueGeneratedOnAdd();
            e.Property(x => x.EventCode).HasMaxLength(80);
            e.Property(x => x.NotificationType).HasMaxLength(20);
            e.Property(x => x.TemplateCode).HasMaxLength(80);
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => x.EventCode).IsUnique().HasDatabaseName("UQ_NotificationSettings_EventCode");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<NotificationSettingRecipient>(e =>
        {
            e.ToTable("NotificationSettingRecipients");
            e.HasKey(x => x.NotificationSettingRecipientId);
            e.Property(x => x.NotificationSettingRecipientId).ValueGeneratedOnAdd();
            e.Property(x => x.RecipientRuleCode).HasMaxLength(50);
            e.HasIndex(x => new { x.NotificationSettingId, x.RecipientRuleCode })
                .IsUnique()
                .HasDatabaseName("UQ_NotificationSettingRecipients_Rule");
            e.HasOne<NotificationSetting>().WithMany().HasForeignKey(x => x.NotificationSettingId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<MailOutbox>(e =>
        {
            e.ToTable("MailOutbox");
            e.HasKey(x => x.MailOutboxId);
            e.Property(x => x.MailOutboxId).ValueGeneratedOnAdd();
            e.Property(x => x.EnvironmentCode).HasMaxLength(20);
            e.Property(x => x.EventCode).HasMaxLength(80);
            e.Property(x => x.BusinessEventKey).HasMaxLength(200);
            e.Property(x => x.AggregateType).HasMaxLength(80);
            e.Property(x => x.AggregateId).HasMaxLength(100);
            e.Property(x => x.RecipientKey).HasMaxLength(200);
            e.Property(x => x.RecipientEmail).HasMaxLength(320);
            e.Property(x => x.NormalizedRecipientEmail)
                .HasMaxLength(320)
                .HasComputedColumnSql("LOWER(LTRIM(RTRIM([RecipientEmail])))", stored: true);
            e.Property(x => x.TemplateCode).HasMaxLength(80);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.LastErrorCode).HasMaxLength(100);
            e.Property(x => x.LastErrorMessage).HasMaxLength(2000);
            e.HasIndex(x => new { x.BusinessEventKey, x.RecipientKey })
                .IsUnique()
                .HasDatabaseName("UX_MailOutbox_BusinessEvent_RecipientKey");
            e.HasIndex(x => new { x.BusinessEventKey, x.NormalizedRecipientEmail })
                .IsUnique()
                .HasFilter("[RecipientEmail] IS NOT NULL")
                .HasDatabaseName("UX_MailOutbox_BusinessEvent_NormalizedRecipientEmail");
            e.HasIndex(x => new { x.Status, x.AvailableAt, x.MailOutboxId }).HasDatabaseName("IX_MailOutbox_Dispatch");
            e.HasIndex(x => new { x.AggregateType, x.AggregateId, x.CreatedAt }).HasDatabaseName("IX_MailOutbox_Aggregate");
            e.HasIndex(x => x.CorrelationId).HasDatabaseName("IX_MailOutbox_Correlation");
            e.HasOne<NotificationEnvironmentPolicy>().WithMany().HasForeignKey(x => x.EnvironmentCode).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<NotificationSetting>().WithMany().HasPrincipalKey(x => x.EventCode).HasForeignKey(x => x.EventCode).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.RecipientEmploymentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<MailDeliveryLog>(e =>
        {
            e.ToTable("MailDeliveryLogs");
            e.HasKey(x => x.MailDeliveryLogId);
            e.Property(x => x.MailDeliveryLogId).ValueGeneratedOnAdd();
            e.Property(x => x.Provider).HasMaxLength(80);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.ProviderMessageId).HasMaxLength(200);
            e.Property(x => x.ErrorCode).HasMaxLength(100);
            e.Property(x => x.ErrorMessage).HasMaxLength(2000);
            e.HasIndex(x => new { x.MailOutboxId, x.AttemptNumber })
                .IsUnique()
                .HasDatabaseName("UQ_MailDeliveryLogs_Attempt");
            e.HasIndex(x => new { x.Status, x.AttemptedAt }).HasDatabaseName("IX_MailDeliveryLogs_Status_Attempted");
            e.HasOne<MailOutbox>().WithMany().HasForeignKey(x => x.MailOutboxId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
