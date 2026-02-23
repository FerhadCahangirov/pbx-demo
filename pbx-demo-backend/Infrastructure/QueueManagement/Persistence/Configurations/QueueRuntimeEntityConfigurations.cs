using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Infrastructure.QueueManagement.Persistence.Configurations;

internal sealed class QueueCallEntityConfiguration : IEntityTypeConfiguration<QueueCallEntity>
{
    public void Configure(EntityTypeBuilder<QueueCallEntity> entity)
    {
        entity.ToTable("QueueCall");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.CdrId).HasMaxLength(128);
        entity.Property(x => x.CallHistoryId).HasMaxLength(128);
        entity.Property(x => x.MainCallHistoryId).HasMaxLength(128);
        entity.Property(x => x.CorrelationKey).HasMaxLength(256).IsRequired();
        entity.Property(x => x.CallerNumber).HasMaxLength(64);
        entity.Property(x => x.CallerName).HasMaxLength(256);
        entity.Property(x => x.CalleeNumber).HasMaxLength(64);
        entity.Property(x => x.CalleeName).HasMaxLength(256);
        entity.Property(x => x.Direction).HasMaxLength(32);
        entity.Property(x => x.CurrentStatus).HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.Disposition).HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.RawCurrentJson).HasColumnType("nvarchar(max)");

        entity.HasIndex(x => new { x.CorrelationKey });
        entity.HasIndex(x => new { x.PbxCallId }).HasFilter("[PbxCallId] IS NOT NULL");
        entity.HasIndex(x => new { x.CdrId }).HasFilter("[CdrId] IS NOT NULL");
        entity.HasIndex(x => new { x.QueueId, x.CurrentStatus });
        entity.HasIndex(x => new { x.QueueId, x.QueuedAtUtc });
        entity.HasIndex(x => new { x.CompletedAtUtc });

        entity
            .HasOne(x => x.Queue)
            .WithMany()
            .HasForeignKey(x => x.QueueId)
            .OnDelete(DeleteBehavior.Restrict);

        entity
            .HasOne(x => x.AnsweredByExtension)
            .WithMany()
            .HasForeignKey(x => x.AnsweredByExtensionId)
            .OnDelete(DeleteBehavior.Restrict);

        entity
            .HasOne(x => x.LastAgentExtension)
            .WithMany()
            .HasForeignKey(x => x.LastAgentExtensionId)
            .OnDelete(DeleteBehavior.Restrict);

        entity
            .HasMany(x => x.Events)
            .WithOne(x => x.QueueCall)
            .HasForeignKey(x => x.QueueCallId)
            .OnDelete(DeleteBehavior.Restrict);

        entity
            .HasMany(x => x.HistoryRows)
            .WithOne(x => x.QueueCall)
            .HasForeignKey(x => x.QueueCallId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class QueueCallEventEntityConfiguration : IEntityTypeConfiguration<QueueCallEventEntity>
{
    public void Configure(EntityTypeBuilder<QueueCallEventEntity> entity)
    {
        entity.ToTable("QueueCallEvent");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.Source).HasMaxLength(32).IsRequired();
        entity.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        entity.Property(x => x.ExternalEventId).HasMaxLength(256);
        entity.Property(x => x.OrderingKey).HasMaxLength(256).IsRequired();
        entity.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        entity.Property(x => x.PayloadHash).HasColumnType("varbinary(32)");
        entity.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.ProcessingStatus).HasConversion<string>().HasMaxLength(16);
        entity.Property(x => x.LastError).HasMaxLength(2048);

        entity.HasIndex(x => new { x.IdempotencyKey }).IsUnique();
        entity.HasIndex(x => new { x.ProcessingStatus, x.NextAttemptAtUtc });
        entity.HasIndex(x => new { x.OrderingKey, x.EventAtUtc, x.Id });
        entity.HasIndex(x => new { x.QueueCallId, x.EventAtUtc });

        entity
            .HasOne(x => x.QueueCall)
            .WithMany(x => x.Events)
            .HasForeignKey(x => x.QueueCallId)
            .OnDelete(DeleteBehavior.Restrict);

        entity
            .HasOne(x => x.Queue)
            .WithMany()
            .HasForeignKey(x => x.QueueId)
            .OnDelete(DeleteBehavior.Restrict);

        entity
            .HasOne(x => x.Extension)
            .WithMany()
            .HasForeignKey(x => x.ExtensionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class QueueCallHistoryEntityConfiguration : IEntityTypeConfiguration<QueueCallHistoryEntity>
{
    public void Configure(EntityTypeBuilder<QueueCallHistoryEntity> entity)
    {
        entity.ToTable("QueueCallHistory");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.SourceRecordType).HasMaxLength(24).IsRequired();
        entity.Property(x => x.CdrId).HasMaxLength(128);
        entity.Property(x => x.CallHistoryId).HasMaxLength(128);
        entity.Property(x => x.MainCallHistoryId).HasMaxLength(128);
        entity.Property(x => x.Direction).HasMaxLength(32);
        entity.Property(x => x.Status).HasMaxLength(64);
        entity.Property(x => x.Reason).HasMaxLength(512);
        entity.Property(x => x.CallType).HasMaxLength(64);
        entity.Property(x => x.SourceDn).HasMaxLength(64);
        entity.Property(x => x.SourceDisplayName).HasMaxLength(256);
        entity.Property(x => x.SourceCallerId).HasMaxLength(128);
        entity.Property(x => x.DestinationDn).HasMaxLength(64);
        entity.Property(x => x.DestinationDisplayName).HasMaxLength(256);
        entity.Property(x => x.DestinationCallerId).HasMaxLength(128);
        entity.Property(x => x.ActionDn).HasMaxLength(64);
        entity.Property(x => x.ActionDnDisplayName).HasMaxLength(256);
        entity.Property(x => x.ActionDnCallerId).HasMaxLength(128);
        entity.Property(x => x.CallCost).HasColumnType("decimal(18,4)");
        entity.Property(x => x.RecordingUrl).HasMaxLength(1024);
        entity.Property(x => x.Transcription).HasColumnType("nvarchar(max)");
        entity.Property(x => x.RawJson).HasColumnType("nvarchar(max)").IsRequired();

        entity.HasIndex(x => new { x.SourceRecordType, x.SegmentId, x.CdrId });
        entity.HasIndex(x => new { x.SourceRecordType, x.CallHistoryId, x.SegmentId });
        entity.HasIndex(x => new { x.QueueId, x.SegmentStartAtUtc });
        entity.HasIndex(x => new { x.CdrId });
        entity.HasIndex(x => new { x.PbxCallId });

        entity
            .HasOne(x => x.QueueCall)
            .WithMany(x => x.HistoryRows)
            .HasForeignKey(x => x.QueueCallId)
            .OnDelete(DeleteBehavior.Restrict);

        entity
            .HasOne(x => x.Queue)
            .WithMany()
            .HasForeignKey(x => x.QueueId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class QueueAgentActivityEntityConfiguration : IEntityTypeConfiguration<QueueAgentActivityEntity>
{
    public void Configure(EntityTypeBuilder<QueueAgentActivityEntity> entity)
    {
        entity.ToTable("QueueAgentActivity");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.ActivityType).HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.ActivityStatus).HasMaxLength(32);
        entity.Property(x => x.Source).HasMaxLength(32).IsRequired();
        entity.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RawJson).HasColumnType("nvarchar(max)");

        entity.HasIndex(x => new { x.IdempotencyKey }).IsUnique();
        entity.HasIndex(x => new { x.ExtensionId, x.OccurredAtUtc });
        entity.HasIndex(x => new { x.QueueId, x.OccurredAtUtc });
        entity.HasIndex(x => new { x.QueueCallId });

        entity
            .HasOne(x => x.Queue)
            .WithMany()
            .HasForeignKey(x => x.QueueId)
            .OnDelete(DeleteBehavior.Restrict);

        entity
            .HasOne(x => x.Extension)
            .WithMany()
            .HasForeignKey(x => x.ExtensionId)
            .OnDelete(DeleteBehavior.Restrict);

        entity
            .HasOne(x => x.QueueCall)
            .WithMany()
            .HasForeignKey(x => x.QueueCallId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class QueueWaitingSnapshotEntityConfiguration : IEntityTypeConfiguration<QueueWaitingSnapshotEntity>
{
    public void Configure(EntityTypeBuilder<QueueWaitingSnapshotEntity> entity)
    {
        entity.ToTable("QueueWaitingSnapshot");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.CorrelationKey).HasMaxLength(256);
        entity.Property(x => x.CallerNumber).HasMaxLength(64);
        entity.Property(x => x.CallerName).HasMaxLength(256);

        entity.HasIndex(x => new { x.QueueId, x.SnapshotKey, x.WaitOrder }).IsUnique();
        entity.HasIndex(x => new { x.QueueId, x.CapturedAtUtc });
        entity.HasIndex(x => new { x.QueueId, x.SnapshotKey, x.CorrelationKey }).HasFilter("[CorrelationKey] IS NOT NULL");

        entity
            .HasOne(x => x.Queue)
            .WithMany()
            .HasForeignKey(x => x.QueueId)
            .OnDelete(DeleteBehavior.Cascade);

        entity
            .HasOne(x => x.QueueCall)
            .WithMany()
            .HasForeignKey(x => x.QueueCallId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class QueueAnalyticsBucketHourEntityConfiguration : IEntityTypeConfiguration<QueueAnalyticsBucketHourEntity>
{
    public void Configure(EntityTypeBuilder<QueueAnalyticsBucketHourEntity> entity)
    {
        entity.ToTable("QueueAnalyticsBucketHour");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
        entity.HasIndex(x => new { x.QueueId, x.BucketStartUtc }).IsUnique();
        entity.HasIndex(x => new { x.BucketStartUtc, x.QueueId });
    }
}

internal sealed class QueueAnalyticsBucketDayEntityConfiguration : IEntityTypeConfiguration<QueueAnalyticsBucketDayEntity>
{
    public void Configure(EntityTypeBuilder<QueueAnalyticsBucketDayEntity> entity)
    {
        entity.ToTable("QueueAnalyticsBucketDay");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
        entity.HasIndex(x => new { x.QueueId, x.BucketDate }).IsUnique();
        entity.HasIndex(x => new { x.BucketDate, x.QueueId });
    }
}

internal sealed class OutboxMessageEntityConfiguration : IEntityTypeConfiguration<OutboxMessageEntity>
{
    public void Configure(EntityTypeBuilder<OutboxMessageEntity> entity)
    {
        entity.ToTable("QueueOutboxMessage");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.Topic).HasMaxLength(128).IsRequired();
        entity.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.LastError).HasMaxLength(2048);

        entity.HasIndex(x => new { x.PublishedAtUtc, x.Id });
        entity.HasIndex(x => new { x.Topic, x.CreatedAtUtc });
    }
}

internal sealed class XapiSyncCheckpointEntityConfiguration : IEntityTypeConfiguration<XapiSyncCheckpointEntity>
{
    public void Configure(EntityTypeBuilder<XapiSyncCheckpointEntity> entity)
    {
        entity.ToTable("XapiSyncCheckpoint");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.StreamName).HasMaxLength(128).IsRequired();
        entity.Property(x => x.PartitionKey).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CursorValue).HasMaxLength(2048);

        entity.HasIndex(x => new { x.StreamName, x.PartitionKey }).IsUnique();
    }
}
