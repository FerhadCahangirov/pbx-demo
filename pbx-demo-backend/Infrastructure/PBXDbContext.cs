using CallControl.Api.Domain;
using Microsoft.EntityFrameworkCore;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Infrastructure;

public class PBXDbContext : DbContext
{
    public PBXDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<AppUserEntity> Users => Set<AppUserEntity>();
    public DbSet<AppDepartmentEntity> Departments => Set<AppDepartmentEntity>();
    public DbSet<AppDepartmentMembershipEntity> DepartmentMemberships => Set<AppDepartmentMembershipEntity>();
    public DbSet<AppCallCdrEntity> CallCdrs => Set<AppCallCdrEntity>();
    public DbSet<AppCallCdrStatusHistoryEntity> CallCdrStatusHistory => Set<AppCallCdrStatusHistoryEntity>();

    public DbSet<QueueEntity> Queues => Set<QueueEntity>();
    public DbSet<QueueSettingsEntity> QueueSettings => Set<QueueSettingsEntity>();
    public DbSet<ExtensionEntity> Extensions => Set<ExtensionEntity>();
    public DbSet<QueueAgentEntity> QueueAgents => Set<QueueAgentEntity>();
    public DbSet<QueueScheduleEntity> QueueSchedules => Set<QueueScheduleEntity>();
    public DbSet<QueueWebhookMappingEntity> QueueWebhookMappings => Set<QueueWebhookMappingEntity>();

    public DbSet<QueueCallEntity> QueueCalls => Set<QueueCallEntity>();
    public DbSet<QueueCallEventEntity> QueueCallEvents => Set<QueueCallEventEntity>();
    public DbSet<QueueCallHistoryEntity> QueueCallHistory => Set<QueueCallHistoryEntity>();
    public DbSet<QueueAgentActivityEntity> QueueAgentActivities => Set<QueueAgentActivityEntity>();
    public DbSet<QueueWaitingSnapshotEntity> QueueWaitingSnapshots => Set<QueueWaitingSnapshotEntity>();

    public DbSet<QueueAnalyticsBucketHourEntity> QueueAnalyticsBucketHours => Set<QueueAnalyticsBucketHourEntity>();
    public DbSet<QueueAnalyticsBucketDayEntity> QueueAnalyticsBucketDays => Set<QueueAnalyticsBucketDayEntity>();

    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();
    public DbSet<XapiSyncCheckpointEntity> XapiSyncCheckpoints => Set<XapiSyncCheckpointEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUserEntity>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(v => v.Id);
            entity.Property(v => v.Username).HasMaxLength(128);
            entity.Property(v => v.PasswordHash).HasMaxLength(512);
            entity.Property(v => v.FirstName).HasMaxLength(128);
            entity.Property(v => v.LastName).HasMaxLength(128);
            entity.Property(v => v.EmailAddress).HasMaxLength(256);
            entity.Property(v => v.OwnedExtension).HasMaxLength(32);
            entity.Property(v => v.ControlDn).HasMaxLength(64);
            entity.Property(v => v.Language).HasMaxLength(16);
            entity.Property(v => v.PromptSet).HasMaxLength(128);
            entity.Property(v => v.VmEmailOptions).HasMaxLength(64);
            entity.Property(v => v.ClickToCallId).HasMaxLength(128);
            entity.Property(v => v.WebMeetingFriendlyName).HasMaxLength(128);
            entity.Property(v => v.SipUsername).HasMaxLength(128);
            entity.Property(v => v.SipAuthId).HasMaxLength(128);
            entity.Property(v => v.SipPassword).HasMaxLength(256);
            entity.Property(v => v.SipDisplayName).HasMaxLength(256);
            entity.Property(v => v.Role).HasConversion<string>().HasMaxLength(32);

            entity.HasIndex(v => v.Username).IsUnique();
            entity.HasIndex(v => v.EmailAddress).IsUnique();
            entity.HasIndex(v => v.OwnedExtension);
            entity.HasIndex(v => v.ThreeCxUserId);
        });

        modelBuilder.Entity<AppDepartmentEntity>(entity =>
        {
            entity.ToTable("Departments");
            entity.HasKey(v => v.Id);
            entity.Property(v => v.Name).HasMaxLength(128);
            entity.Property(v => v.ThreeCxGroupNumber).HasMaxLength(32);
            entity.Property(v => v.Language).HasMaxLength(16);
            entity.Property(v => v.TimeZoneId).HasMaxLength(16);
            entity.Property(v => v.PromptSet).HasMaxLength(128);
            entity.Property(v => v.PropsJson).HasMaxLength(8000);
            entity.Property(v => v.RoutingJson).HasMaxLength(8000);
            entity.Property(v => v.LiveChatLink).HasMaxLength(256);
            entity.Property(v => v.LiveChatWebsite).HasMaxLength(512);

            entity.HasIndex(v => v.Name).IsUnique();
            entity.HasIndex(v => v.ThreeCxGroupId).IsUnique();
        });

        modelBuilder.Entity<AppDepartmentMembershipEntity>(entity =>
        {
            entity.ToTable("DepartmentMemberships");
            entity.HasKey(v => v.Id);
            entity.Property(v => v.ThreeCxRoleName).HasMaxLength(64);
            entity.HasIndex(v => new { v.AppUserId, v.AppDepartmentId }).IsUnique();

            entity
                .HasOne(v => v.AppUser)
                .WithMany(v => v.DepartmentMemberships)
                .HasForeignKey(v => v.AppUserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity
                .HasOne(v => v.AppDepartment)
                .WithMany(v => v.UserMemberships)
                .HasForeignKey(v => v.AppDepartmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppCallCdrEntity>(entity =>
        {
            entity.ToTable("CallCdrs");
            entity.HasKey(v => v.Id);
            entity.Property(v => v.Source).HasConversion<string>().HasMaxLength(24);
            entity.Property(v => v.OperatorUsername).HasMaxLength(128);
            entity.Property(v => v.OperatorExtension).HasMaxLength(32);
            entity.Property(v => v.TrackingKey).HasMaxLength(160);
            entity.Property(v => v.CallScopeId).HasMaxLength(64);
            entity.Property(v => v.Direction).HasConversion<string>().HasMaxLength(16);
            entity.Property(v => v.Status).HasMaxLength(32);
            entity.Property(v => v.RemoteParty).HasMaxLength(128);
            entity.Property(v => v.RemoteName).HasMaxLength(256);
            entity.Property(v => v.EndReason).HasMaxLength(128);

            entity.HasIndex(v => new { v.OperatorUserId, v.StartedAtUtc });
            entity.HasIndex(v => new { v.Source, v.TrackingKey });
            entity.HasIndex(v => new { v.OperatorUserId, v.PbxCallId });
            entity.HasIndex(v => v.IsActive);

            entity
                .HasOne(v => v.OperatorUser)
                .WithMany(v => v.CallCdrs)
                .HasForeignKey(v => v.OperatorUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppCallCdrStatusHistoryEntity>(entity =>
        {
            entity.ToTable("CallCdrStatusHistory");
            entity.HasKey(v => v.Id);
            entity.Property(v => v.Status).HasMaxLength(32);
            entity.Property(v => v.EventType).HasMaxLength(64);
            entity.Property(v => v.EventReason).HasMaxLength(128);

            entity.HasIndex(v => new { v.CallCdrId, v.OccurredAtUtc });

            entity
                .HasOne(v => v.CallCdr)
                .WithMany(v => v.StatusHistory)
                .HasForeignKey(v => v.CallCdrId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PBXDbContext).Assembly);
    }
}
