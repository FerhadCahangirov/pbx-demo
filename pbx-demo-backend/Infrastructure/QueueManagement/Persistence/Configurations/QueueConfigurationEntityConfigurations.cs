using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Infrastructure.QueueManagement.Persistence.Configurations;

internal sealed class QueueEntityConfiguration : IEntityTypeConfiguration<QueueEntity>
{
    public void Configure(EntityTypeBuilder<QueueEntity> entity)
    {
        entity.ToTable("Queue");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.QueueNumber).HasMaxLength(32).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(256).IsRequired();
        entity.Property(x => x.RawJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.LastXapiHash).HasColumnType("varbinary(32)");

        entity.HasIndex(x => new { x.PbxQueueId }).IsUnique();
        entity.HasIndex(x => new { x.QueueNumber }).IsUnique().HasFilter("[IsDeleted] = 0");
        entity.HasIndex(x => new { x.Name });
        entity.HasIndex(x => new { x.IsDeleted });

        entity.HasQueryFilter(x => !x.IsDeleted);

        entity
            .HasOne(x => x.Settings)
            .WithOne(x => x.Queue)
            .HasForeignKey<QueueSettingsEntity>(x => x.QueueId)
            .OnDelete(DeleteBehavior.Cascade);

        entity
            .HasMany(x => x.Agents)
            .WithOne(x => x.Queue)
            .HasForeignKey(x => x.QueueId)
            .OnDelete(DeleteBehavior.Cascade);

        entity
            .HasMany(x => x.Schedules)
            .WithOne(x => x.Queue)
            .HasForeignKey(x => x.QueueId)
            .OnDelete(DeleteBehavior.Cascade);

        entity
            .HasMany(x => x.WebhookMappings)
            .WithOne(x => x.Queue)
            .HasForeignKey(x => x.QueueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class QueueSettingsEntityConfiguration : IEntityTypeConfiguration<QueueSettingsEntity>
{
    public void Configure(EntityTypeBuilder<QueueSettingsEntity> entity)
    {
        entity.ToTable("QueueSettings");
        entity.HasKey(x => x.QueueId);

        entity.Property(x => x.CallbackPrefix).HasMaxLength(32);
        entity.Property(x => x.GreetingFile).HasMaxLength(512);
        entity.Property(x => x.IntroFile).HasMaxLength(512);
        entity.Property(x => x.OnHoldFile).HasMaxLength(512);
        entity.Property(x => x.PromptSet).HasMaxLength(128);
        entity.Property(x => x.PollingStrategy).HasMaxLength(64);
        entity.Property(x => x.RecordingMode).HasMaxLength(64);
        entity.Property(x => x.NotifyCodesJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.ResetStatsFrequency).HasMaxLength(32);
        entity.Property(x => x.ResetStatsDayOfWeek).HasMaxLength(16);
        entity.Property(x => x.ResetStatsTime).HasMaxLength(16);
        entity.Property(x => x.BreakRouteJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.HolidaysRouteJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.OutOfOfficeRouteJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.ForwardNoAnswerJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.TranscriptionMode).HasMaxLength(32);
        entity.Property(x => x.ChatOwnershipType).HasMaxLength(32);
        entity.Property(x => x.CallUsRequirement).HasMaxLength(32);
        entity.Property(x => x.ClickToCallId).HasMaxLength(128);
    }
}

internal sealed class ExtensionEntityConfiguration : IEntityTypeConfiguration<ExtensionEntity>
{
    public void Configure(EntityTypeBuilder<ExtensionEntity> entity)
    {
        entity.ToTable("QueueExtension");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.ExtensionNumber).HasMaxLength(32).IsRequired();
        entity.Property(x => x.DisplayName).HasMaxLength(256);
        entity.Property(x => x.EmailAddress).HasMaxLength(256);
        entity.Property(x => x.QueueStatus).HasMaxLength(32);
        entity.Property(x => x.RawJson).HasColumnType("nvarchar(max)");

        entity.HasIndex(x => new { x.PbxUserId }).IsUnique();
        entity.HasIndex(x => new { x.ExtensionNumber }).IsUnique();
        entity.HasIndex(x => new { x.QueueStatus });
        entity.HasIndex(x => new { x.DisplayName });

        entity
            .HasMany(x => x.QueueMemberships)
            .WithOne(x => x.Extension)
            .HasForeignKey(x => x.ExtensionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class QueueAgentEntityConfiguration : IEntityTypeConfiguration<QueueAgentEntity>
{
    public void Configure(EntityTypeBuilder<QueueAgentEntity> entity)
    {
        entity.ToTable("QueueAgent");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.AgentNumberSnapshot).HasMaxLength(32).IsRequired();
        entity.Property(x => x.AgentNameSnapshot).HasMaxLength(256);
        entity.Property(x => x.SkillGroup).HasMaxLength(64);
        entity.Property(x => x.AssignmentSource).HasMaxLength(32).IsRequired();

        entity.HasIndex(x => new { x.QueueId, x.ExtensionId }).IsUnique().HasFilter("[IsDeleted] = 0");
        entity.HasIndex(x => x.ExtensionId);
        entity.HasIndex(x => new { x.QueueId, x.IsAgentMember });
        entity.HasIndex(x => new { x.QueueId, x.IsQueueManager });
        entity.HasIndex(x => new { x.QueueId, x.IsDeleted });

        entity.HasQueryFilter(x => !x.IsDeleted);

        entity
            .HasOne(x => x.Queue)
            .WithMany(x => x.Agents)
            .HasForeignKey(x => x.QueueId)
            .OnDelete(DeleteBehavior.Cascade);

        entity
            .HasOne(x => x.Extension)
            .WithMany(x => x.QueueMemberships)
            .HasForeignKey(x => x.ExtensionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class QueueScheduleEntityConfiguration : IEntityTypeConfiguration<QueueScheduleEntity>
{
    public void Configure(EntityTypeBuilder<QueueScheduleEntity> entity)
    {
        entity.ToTable("QueueSchedule");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.ScheduleType).HasMaxLength(32).IsRequired();
        entity.Property(x => x.SourceSystem).HasMaxLength(16).IsRequired();
        entity.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
        entity.Property(x => x.RuleJson).HasColumnType("nvarchar(max)");

        entity.HasIndex(x => new { x.QueueId, x.ScheduleType, x.IsEnabled });
        entity.HasIndex(x => new { x.QueueId, x.EffectiveFromDate, x.EffectiveToDate });

        entity
            .HasOne(x => x.Queue)
            .WithMany(x => x.Schedules)
            .HasForeignKey(x => x.QueueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class QueueWebhookMappingEntityConfiguration : IEntityTypeConfiguration<QueueWebhookMappingEntity>
{
    public void Configure(EntityTypeBuilder<QueueWebhookMappingEntity> entity)
    {
        entity.ToTable("QueueWebhookMapping");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.EndpointUrl).HasMaxLength(1024);
        entity.Property(x => x.SecretRef).HasMaxLength(256);
        entity.Property(x => x.EventTypesCsv).HasMaxLength(1024).IsRequired();
        entity.Property(x => x.FilterJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.RetryPolicyJson).HasColumnType("nvarchar(max)");

        entity.HasIndex(x => new { x.QueueId, x.IsEnabled });
        entity.HasIndex(x => x.WebhookEndpointId);

        entity
            .HasOne(x => x.Queue)
            .WithMany(x => x.WebhookMappings)
            .HasForeignKey(x => x.QueueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
