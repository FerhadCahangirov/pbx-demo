using CallControl.Api.Domain;
using CallControl.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CallControl.Api.Services;

public sealed class DatabaseBootstrapper
{
    private readonly IDbContextFactory<PBXDbContext> _dbContextFactory;
    private readonly SoftphoneOptions _options;
    private readonly PasswordHasher _passwordHasher;
    private readonly ILogger<DatabaseBootstrapper> _logger;

    public DatabaseBootstrapper(
        IDbContextFactory<PBXDbContext> dbContextFactory,
        IOptions<SoftphoneOptions> options,
        PasswordHasher passwordHasher,
        ILogger<DatabaseBootstrapper> logger)
    {
        _dbContextFactory = dbContextFactory;
        _options = options.Value;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureCallCdrSchemaAsync(dbContext, cancellationToken);

        if (await dbContext.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var users = new List<AppUserEntity>();
        var seenUsernames = new HashSet<string>(StringComparer.Ordinal);
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configuredUser in _options.Users)
        {
            var username = configuredUser.Username?.Trim() ?? string.Empty;
            var password = configuredUser.Password ?? string.Empty;
            if (string.IsNullOrWhiteSpace(username)
                || string.IsNullOrWhiteSpace(password)
                || !seenUsernames.Add(username))
            {
                continue;
            }

            var emailAddress = (configuredUser.EmailAddress ?? string.Empty).Trim();
            if (emailAddress.Length == 0)
            {
                emailAddress = $"{username}@local.crm";
            }

            if (!seenEmails.Add(emailAddress))
            {
                emailAddress = $"{username}.{Guid.NewGuid():N}@local.crm";
            }

            users.Add(new AppUserEntity
            {
                Username = username,
                PasswordHash = _passwordHasher.HashPassword(password),
                FirstName = configuredUser.FirstName?.Trim() ?? string.Empty,
                LastName = configuredUser.LastName?.Trim() ?? string.Empty,
                EmailAddress = emailAddress,
                OwnedExtension = configuredUser.OwnedExtension?.Trim() ?? string.Empty,
                ControlDn = string.IsNullOrWhiteSpace(configuredUser.ControlDn) ? null : configuredUser.ControlDn.Trim(),
                Role = configuredUser.IsSupervisor ? AppUserRole.Supervisor : AppUserRole.User,
                Language = configuredUser.Language?.Trim() ?? "EN",
                PromptSet = configuredUser.PromptSet?.Trim(),
                VmEmailOptions = configuredUser.VmEmailOptions?.Trim() ?? "Notification",
                SendEmailMissedCalls = configuredUser.SendEmailMissedCalls,
                Require2Fa = configuredUser.Require2Fa,
                CallUsEnableChat = configuredUser.CallUsEnableChat,
                ClickToCallId = configuredUser.ClickToCallId?.Trim(),
                WebMeetingFriendlyName = configuredUser.WebMeetingFriendlyName?.Trim(),
                SipUsername = configuredUser.Sip.Username?.Trim(),
                SipAuthId = configuredUser.Sip.AuthId?.Trim(),
                SipPassword = configuredUser.Sip.Password?.Trim(),
                SipDisplayName = configuredUser.Sip.DisplayName?.Trim(),
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        if (users.Count == 0)
        {
            users.Add(new AppUserEntity
            {
                Username = "supervisor",
                PasswordHash = _passwordHasher.HashPassword("Supervisor123!"),
                FirstName = "System",
                LastName = "Supervisor",
                EmailAddress = "supervisor@local.crm",
                OwnedExtension = string.Empty,
                Role = AppUserRole.Supervisor,
                Language = "EN",
                VmEmailOptions = "Notification",
                SendEmailMissedCalls = true,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            _logger.LogWarning(
                "Database was empty. Seeded fallback supervisor account username '{Username}'. Update this credential immediately.",
                "supervisor");
        }

        if (!users.Any(v => v.Role == AppUserRole.Supervisor))
        {
            users[0].Role = AppUserRole.Supervisor;
        }

        dbContext.Users.AddRange(users);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static Task EnsureCallCdrSchemaAsync(PBXDbContext dbContext, CancellationToken cancellationToken)
    {
        const string sql = """
IF OBJECT_ID(N'[CallCdrs]', N'U') IS NULL
BEGIN
    CREATE TABLE [CallCdrs]
    (
        [Id] BIGINT IDENTITY(1,1) NOT NULL,
        [Source] NVARCHAR(24) NOT NULL,
        [OperatorUserId] INT NOT NULL,
        [OperatorUsername] NVARCHAR(128) NOT NULL,
        [OperatorExtension] NVARCHAR(32) NOT NULL,
        [TrackingKey] NVARCHAR(160) NOT NULL,
        [CallScopeId] NVARCHAR(64) NULL,
        [ParticipantId] BIGINT NULL,
        [PbxCallId] BIGINT NULL,
        [PbxLegId] BIGINT NULL,
        [Direction] NVARCHAR(16) NOT NULL,
        [Status] NVARCHAR(32) NOT NULL,
        [RemoteParty] NVARCHAR(128) NULL,
        [RemoteName] NVARCHAR(256) NULL,
        [EndReason] NVARCHAR(128) NULL,
        [StartedAtUtc] DATETIMEOFFSET NOT NULL,
        [AnsweredAtUtc] DATETIMEOFFSET NULL,
        [EndedAtUtc] DATETIMEOFFSET NULL,
        [LastStatusAtUtc] DATETIMEOFFSET NOT NULL,
        [IsActive] BIT NOT NULL,
        [CreatedAtUtc] DATETIMEOFFSET NOT NULL,
        [UpdatedAtUtc] DATETIMEOFFSET NOT NULL,
        CONSTRAINT [PK_CallCdrs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CallCdrs_Users_OperatorUserId]
            FOREIGN KEY ([OperatorUserId]) REFERENCES [Users]([Id]) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'[CallCdrStatusHistory]', N'U') IS NULL
BEGIN
    CREATE TABLE [CallCdrStatusHistory]
    (
        [Id] BIGINT IDENTITY(1,1) NOT NULL,
        [CallCdrId] BIGINT NOT NULL,
        [Status] NVARCHAR(32) NOT NULL,
        [EventType] NVARCHAR(64) NOT NULL,
        [EventReason] NVARCHAR(128) NULL,
        [OccurredAtUtc] DATETIMEOFFSET NOT NULL,
        [CreatedAtUtc] DATETIMEOFFSET NOT NULL,
        CONSTRAINT [PK_CallCdrStatusHistory] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CallCdrStatusHistory_CallCdrs_CallCdrId]
            FOREIGN KEY ([CallCdrId]) REFERENCES [CallCdrs]([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CallCdrs_OperatorUserId_StartedAtUtc' AND object_id = OBJECT_ID(N'[CallCdrs]'))
    CREATE INDEX [IX_CallCdrs_OperatorUserId_StartedAtUtc] ON [CallCdrs]([OperatorUserId], [StartedAtUtc]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CallCdrs_Source_TrackingKey' AND object_id = OBJECT_ID(N'[CallCdrs]'))
    CREATE INDEX [IX_CallCdrs_Source_TrackingKey] ON [CallCdrs]([Source], [TrackingKey]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CallCdrs_OperatorUserId_PbxCallId' AND object_id = OBJECT_ID(N'[CallCdrs]'))
    CREATE INDEX [IX_CallCdrs_OperatorUserId_PbxCallId] ON [CallCdrs]([OperatorUserId], [PbxCallId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CallCdrs_IsActive' AND object_id = OBJECT_ID(N'[CallCdrs]'))
    CREATE INDEX [IX_CallCdrs_IsActive] ON [CallCdrs]([IsActive]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CallCdrStatusHistory_CallCdrId_OccurredAtUtc' AND object_id = OBJECT_ID(N'[CallCdrStatusHistory]'))
    CREATE INDEX [IX_CallCdrStatusHistory_CallCdrId_OccurredAtUtc] ON [CallCdrStatusHistory]([CallCdrId], [OccurredAtUtc]);
""";

        return dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }
}
