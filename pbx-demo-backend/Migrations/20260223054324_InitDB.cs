using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace pbx_demo_backend.Migrations
{
    /// <inheritdoc />
    public partial class InitDB : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ThreeCxGroupId = table.Column<int>(type: "int", nullable: false),
                    ThreeCxGroupNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Language = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PromptSet = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DisableCustomPrompt = table.Column<bool>(type: "bit", nullable: false),
                    PropsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    RoutingJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    LiveChatLink = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LiveChatWebsite = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ThreeCxWebsiteLinkId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "XapiSyncCheckpoint",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StreamName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PartitionKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CursorValue = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    LastSuccessfulSyncAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XapiSyncCheckpoint", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Queue",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PbxQueueId = table.Column<int>(type: "int", nullable: false),
                    QueueNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IsRegistered = table.Column<bool>(type: "bit", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    LastXapiSyncAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastXapiHash = table.Column<byte[]>(type: "varbinary(32)", nullable: true),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Queue", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QueueAnalyticsBucketDay",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QueueId = table.Column<long>(type: "bigint", nullable: false),
                    BucketDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TotalCalls = table.Column<long>(type: "bigint", nullable: false),
                    AnsweredCalls = table.Column<long>(type: "bigint", nullable: false),
                    AbandonedCalls = table.Column<long>(type: "bigint", nullable: false),
                    MissedCalls = table.Column<long>(type: "bigint", nullable: false),
                    WaitingMsSum = table.Column<long>(type: "bigint", nullable: false),
                    WaitingMsCount = table.Column<long>(type: "bigint", nullable: false),
                    TalkingMsSum = table.Column<long>(type: "bigint", nullable: false),
                    TalkingMsCount = table.Column<long>(type: "bigint", nullable: false),
                    SlaEligibleCalls = table.Column<long>(type: "bigint", nullable: false),
                    SlaWithinThresholdCalls = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueAnalyticsBucketDay", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QueueAnalyticsBucketHour",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QueueId = table.Column<long>(type: "bigint", nullable: false),
                    BucketStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TotalCalls = table.Column<long>(type: "bigint", nullable: false),
                    AnsweredCalls = table.Column<long>(type: "bigint", nullable: false),
                    AbandonedCalls = table.Column<long>(type: "bigint", nullable: false),
                    MissedCalls = table.Column<long>(type: "bigint", nullable: false),
                    WaitingMsSum = table.Column<long>(type: "bigint", nullable: false),
                    WaitingMsCount = table.Column<long>(type: "bigint", nullable: false),
                    TalkingMsSum = table.Column<long>(type: "bigint", nullable: false),
                    TalkingMsCount = table.Column<long>(type: "bigint", nullable: false),
                    SlaEligibleCalls = table.Column<long>(type: "bigint", nullable: false),
                    SlaWithinThresholdCalls = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueAnalyticsBucketHour", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QueueExtension",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PbxUserId = table.Column<int>(type: "int", nullable: false),
                    ExtensionNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: true),
                    Internal = table.Column<bool>(type: "bit", nullable: true),
                    IsRegistered = table.Column<bool>(type: "bit", nullable: true),
                    QueueStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastXapiSyncAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueExtension", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QueueOutboxMessage",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Topic = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueOutboxMessage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EmailAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OwnedExtension = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ControlDn = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PromptSet = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    VmEmailOptions = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SendEmailMissedCalls = table.Column<bool>(type: "bit", nullable: false),
                    Require2Fa = table.Column<bool>(type: "bit", nullable: false),
                    CallUsEnableChat = table.Column<bool>(type: "bit", nullable: false),
                    ClickToCallId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    WebMeetingFriendlyName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SipUsername = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SipAuthId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SipPassword = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SipDisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ThreeCxUserId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QueueSchedule",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QueueId = table.Column<long>(type: "bigint", nullable: false),
                    ScheduleType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceSystem = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: true),
                    StartLocalTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    EndLocalTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    EffectiveFromDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveToDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    RuleJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueSchedule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueueSchedule_Queue_QueueId",
                        column: x => x.QueueId,
                        principalTable: "Queue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QueueSettings",
                columns: table => new
                {
                    QueueId = table.Column<long>(type: "bigint", nullable: false),
                    AgentAvailabilityMode = table.Column<bool>(type: "bit", nullable: true),
                    AnnouncementIntervalSec = table.Column<int>(type: "int", nullable: true),
                    AnnounceQueuePosition = table.Column<bool>(type: "bit", nullable: true),
                    CallbackEnableTimeSec = table.Column<int>(type: "int", nullable: true),
                    CallbackPrefix = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    EnableIntro = table.Column<bool>(type: "bit", nullable: true),
                    GreetingFile = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    IntroFile = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    OnHoldFile = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    PromptSet = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PlayFullPrompt = table.Column<bool>(type: "bit", nullable: true),
                    PriorityQueue = table.Column<bool>(type: "bit", nullable: true),
                    RingTimeoutSec = table.Column<int>(type: "int", nullable: true),
                    MasterTimeoutSec = table.Column<int>(type: "int", nullable: true),
                    MaxCallersInQueue = table.Column<int>(type: "int", nullable: true),
                    SlaTimeSec = table.Column<int>(type: "int", nullable: true),
                    WrapUpTimeSec = table.Column<int>(type: "int", nullable: true),
                    PollingStrategy = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RecordingMode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    NotifyCodesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResetStatisticsScheduleEnabled = table.Column<bool>(type: "bit", nullable: true),
                    ResetStatsFrequency = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ResetStatsDayOfWeek = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ResetStatsTime = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    BreakRouteJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HolidaysRouteJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OutOfOfficeRouteJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ForwardNoAnswerJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TranscriptionMode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ChatOwnershipType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CallUsEnableChat = table.Column<bool>(type: "bit", nullable: true),
                    CallUsEnablePhone = table.Column<bool>(type: "bit", nullable: true),
                    CallUsEnableVideo = table.Column<bool>(type: "bit", nullable: true),
                    CallUsRequirement = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ClickToCallId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueSettings", x => x.QueueId);
                    table.ForeignKey(
                        name: "FK_QueueSettings_Queue_QueueId",
                        column: x => x.QueueId,
                        principalTable: "Queue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QueueWebhookMapping",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QueueId = table.Column<long>(type: "bigint", nullable: false),
                    WebhookEndpointId = table.Column<long>(type: "bigint", nullable: true),
                    EndpointUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    SecretRef = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EventTypesCsv = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    FilterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RetryPolicyJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LastDeliveryAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastFailureAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueWebhookMapping", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueueWebhookMapping_Queue_QueueId",
                        column: x => x.QueueId,
                        principalTable: "Queue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QueueAgent",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QueueId = table.Column<long>(type: "bigint", nullable: false),
                    ExtensionId = table.Column<long>(type: "bigint", nullable: false),
                    PbxAgentRefId = table.Column<int>(type: "int", nullable: true),
                    AgentNumberSnapshot = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AgentNameSnapshot = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SkillGroup = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsAgentMember = table.Column<bool>(type: "bit", nullable: false),
                    IsQueueManager = table.Column<bool>(type: "bit", nullable: false),
                    AssignmentSource = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    LastXapiSyncAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueAgent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueueAgent_QueueExtension_ExtensionId",
                        column: x => x.ExtensionId,
                        principalTable: "QueueExtension",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueueAgent_Queue_QueueId",
                        column: x => x.QueueId,
                        principalTable: "Queue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QueueCall",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QueueId = table.Column<long>(type: "bigint", nullable: true),
                    AnsweredByExtensionId = table.Column<long>(type: "bigint", nullable: true),
                    LastAgentExtensionId = table.Column<long>(type: "bigint", nullable: true),
                    PbxCallId = table.Column<int>(type: "int", nullable: true),
                    CdrId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CallHistoryId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    MainCallHistoryId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CurrentSegmentId = table.Column<int>(type: "int", nullable: true),
                    CorrelationKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CallerNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CallerName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CalleeNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CalleeName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Direction = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CurrentStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Disposition = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    WaitOrder = table.Column<int>(type: "int", nullable: true),
                    TransferCount = table.Column<int>(type: "int", nullable: false),
                    SlaThresholdSec = table.Column<int>(type: "int", nullable: true),
                    SlaBreached = table.Column<bool>(type: "bit", nullable: true),
                    FirstSeenAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    QueuedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OfferedToAgentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AnsweredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EstablishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AbandonedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    WaitingMs = table.Column<long>(type: "bigint", nullable: true),
                    RingingMs = table.Column<long>(type: "bigint", nullable: true),
                    TalkingMs = table.Column<long>(type: "bigint", nullable: true),
                    WrapUpMs = table.Column<long>(type: "bigint", nullable: true),
                    RawCurrentJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProjectionVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueCall", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueueCall_QueueExtension_AnsweredByExtensionId",
                        column: x => x.AnsweredByExtensionId,
                        principalTable: "QueueExtension",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueueCall_QueueExtension_LastAgentExtensionId",
                        column: x => x.LastAgentExtensionId,
                        principalTable: "QueueExtension",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueueCall_Queue_QueueId",
                        column: x => x.QueueId,
                        principalTable: "Queue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CallCdrs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    OperatorUserId = table.Column<int>(type: "int", nullable: false),
                    OperatorUsername = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OperatorExtension = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TrackingKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    CallScopeId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ParticipantId = table.Column<long>(type: "bigint", nullable: true),
                    PbxCallId = table.Column<long>(type: "bigint", nullable: true),
                    PbxLegId = table.Column<long>(type: "bigint", nullable: true),
                    Direction = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RemoteParty = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RemoteName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EndReason = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AnsweredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastStatusAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallCdrs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CallCdrs_Users_OperatorUserId",
                        column: x => x.OperatorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DepartmentMemberships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AppUserId = table.Column<int>(type: "int", nullable: false),
                    AppDepartmentId = table.Column<int>(type: "int", nullable: false),
                    ThreeCxRoleName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartmentMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DepartmentMemberships_Departments_AppDepartmentId",
                        column: x => x.AppDepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DepartmentMemberships_Users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QueueAgentActivity",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QueueId = table.Column<long>(type: "bigint", nullable: true),
                    ExtensionId = table.Column<long>(type: "bigint", nullable: false),
                    QueueCallId = table.Column<long>(type: "bigint", nullable: true),
                    ActivityType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActivityStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueAgentActivity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueueAgentActivity_QueueCall_QueueCallId",
                        column: x => x.QueueCallId,
                        principalTable: "QueueCall",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueueAgentActivity_QueueExtension_ExtensionId",
                        column: x => x.ExtensionId,
                        principalTable: "QueueExtension",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueueAgentActivity_Queue_QueueId",
                        column: x => x.QueueId,
                        principalTable: "Queue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QueueCallEvent",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QueueCallId = table.Column<long>(type: "bigint", nullable: true),
                    QueueId = table.Column<long>(type: "bigint", nullable: true),
                    ExtensionId = table.Column<long>(type: "bigint", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExternalEventId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    OrderingKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SequenceNo = table.Column<long>(type: "bigint", nullable: true),
                    EventAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayloadHash = table.Column<byte[]>(type: "varbinary(32)", nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProcessingStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ProcessingAttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueCallEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueueCallEvent_QueueCall_QueueCallId",
                        column: x => x.QueueCallId,
                        principalTable: "QueueCall",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueueCallEvent_QueueExtension_ExtensionId",
                        column: x => x.ExtensionId,
                        principalTable: "QueueExtension",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueueCallEvent_Queue_QueueId",
                        column: x => x.QueueId,
                        principalTable: "Queue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QueueCallHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QueueCallId = table.Column<long>(type: "bigint", nullable: true),
                    QueueId = table.Column<long>(type: "bigint", nullable: true),
                    SourceRecordType = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    PbxCallId = table.Column<int>(type: "int", nullable: true),
                    CdrId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CallHistoryId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    MainCallHistoryId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SegmentId = table.Column<int>(type: "int", nullable: true),
                    SegmentType = table.Column<int>(type: "int", nullable: true),
                    SegmentActionId = table.Column<int>(type: "int", nullable: true),
                    SegmentStartAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SegmentEndAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CallAnswered = table.Column<bool>(type: "bit", nullable: true),
                    CallTimeMs = table.Column<long>(type: "bigint", nullable: true),
                    RingingDurationMs = table.Column<long>(type: "bigint", nullable: true),
                    TalkingDurationMs = table.Column<long>(type: "bigint", nullable: true),
                    Direction = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CallType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SourceDn = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SourceDisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SourceCallerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SourceType = table.Column<int>(type: "int", nullable: true),
                    DestinationDn = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DestinationDisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DestinationCallerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DestinationType = table.Column<int>(type: "int", nullable: true),
                    ActionDn = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ActionDnDisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ActionDnCallerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ActionDnType = table.Column<int>(type: "int", nullable: true),
                    CallCost = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    RecordingUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    Transcription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SentimentScore = table.Column<int>(type: "int", nullable: true),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueCallHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueueCallHistory_QueueCall_QueueCallId",
                        column: x => x.QueueCallId,
                        principalTable: "QueueCall",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueueCallHistory_Queue_QueueId",
                        column: x => x.QueueId,
                        principalTable: "Queue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QueueWaitingSnapshot",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QueueId = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    QueueCallId = table.Column<long>(type: "bigint", nullable: true),
                    PbxCallId = table.Column<int>(type: "int", nullable: true),
                    CorrelationKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    WaitOrder = table.Column<int>(type: "int", nullable: false),
                    WaitingMs = table.Column<long>(type: "bigint", nullable: true),
                    CallerNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CallerName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EstimatedOrder = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueWaitingSnapshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueueWaitingSnapshot_QueueCall_QueueCallId",
                        column: x => x.QueueCallId,
                        principalTable: "QueueCall",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueueWaitingSnapshot_Queue_QueueId",
                        column: x => x.QueueId,
                        principalTable: "Queue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CallCdrStatusHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CallCdrId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EventReason = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallCdrStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CallCdrStatusHistory_CallCdrs_CallCdrId",
                        column: x => x.CallCdrId,
                        principalTable: "CallCdrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CallCdrs_IsActive",
                table: "CallCdrs",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_CallCdrs_OperatorUserId_PbxCallId",
                table: "CallCdrs",
                columns: new[] { "OperatorUserId", "PbxCallId" });

            migrationBuilder.CreateIndex(
                name: "IX_CallCdrs_OperatorUserId_StartedAtUtc",
                table: "CallCdrs",
                columns: new[] { "OperatorUserId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CallCdrs_Source_TrackingKey",
                table: "CallCdrs",
                columns: new[] { "Source", "TrackingKey" });

            migrationBuilder.CreateIndex(
                name: "IX_CallCdrStatusHistory_CallCdrId_OccurredAtUtc",
                table: "CallCdrStatusHistory",
                columns: new[] { "CallCdrId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentMemberships_AppDepartmentId",
                table: "DepartmentMemberships",
                column: "AppDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentMemberships_AppUserId_AppDepartmentId",
                table: "DepartmentMemberships",
                columns: new[] { "AppUserId", "AppDepartmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_ThreeCxGroupId",
                table: "Departments",
                column: "ThreeCxGroupId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_XapiSyncCheckpoint_StreamName_PartitionKey",
                table: "XapiSyncCheckpoint",
                columns: new[] { "StreamName", "PartitionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Queue_IsDeleted",
                table: "Queue",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Queue_QueueNumber",
                table: "Queue",
                column: "QueueNumber",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Queue_Name",
                table: "Queue",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Queue_PbxQueueId",
                table: "Queue",
                column: "PbxQueueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueAgent_ExtensionId",
                table: "QueueAgent",
                column: "ExtensionId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueAgent_QueueId_ExtensionId",
                table: "QueueAgent",
                columns: new[] { "QueueId", "ExtensionId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QueueAgent_QueueId_IsAgentMember",
                table: "QueueAgent",
                columns: new[] { "QueueId", "IsAgentMember" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueAgent_QueueId_IsDeleted",
                table: "QueueAgent",
                columns: new[] { "QueueId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueAgent_QueueId_IsQueueManager",
                table: "QueueAgent",
                columns: new[] { "QueueId", "IsQueueManager" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueAgentActivity_ExtensionId_OccurredAtUtc",
                table: "QueueAgentActivity",
                columns: new[] { "ExtensionId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueAgentActivity_IdempotencyKey",
                table: "QueueAgentActivity",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueAgentActivity_QueueCallId",
                table: "QueueAgentActivity",
                column: "QueueCallId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueAgentActivity_QueueId_OccurredAtUtc",
                table: "QueueAgentActivity",
                columns: new[] { "QueueId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueAnalyticsBucketDay_BucketDate_QueueId",
                table: "QueueAnalyticsBucketDay",
                columns: new[] { "BucketDate", "QueueId" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueAnalyticsBucketDay_QueueId_BucketDate",
                table: "QueueAnalyticsBucketDay",
                columns: new[] { "QueueId", "BucketDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueAnalyticsBucketHour_BucketStartUtc_QueueId",
                table: "QueueAnalyticsBucketHour",
                columns: new[] { "BucketStartUtc", "QueueId" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueAnalyticsBucketHour_QueueId_BucketStartUtc",
                table: "QueueAnalyticsBucketHour",
                columns: new[] { "QueueId", "BucketStartUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueCall_AnsweredByExtensionId",
                table: "QueueCall",
                column: "AnsweredByExtensionId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueCall_CdrId",
                table: "QueueCall",
                column: "CdrId",
                filter: "[CdrId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_QueueCall_CompletedAtUtc",
                table: "QueueCall",
                column: "CompletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_QueueCall_CorrelationKey",
                table: "QueueCall",
                column: "CorrelationKey");

            migrationBuilder.CreateIndex(
                name: "IX_QueueCall_QueueId_CurrentStatus",
                table: "QueueCall",
                columns: new[] { "QueueId", "CurrentStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueCall_QueueId_QueuedAtUtc",
                table: "QueueCall",
                columns: new[] { "QueueId", "QueuedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueCall_LastAgentExtensionId",
                table: "QueueCall",
                column: "LastAgentExtensionId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueCall_PbxCallId",
                table: "QueueCall",
                column: "PbxCallId",
                filter: "[PbxCallId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_QueueCallEvent_ExtensionId",
                table: "QueueCallEvent",
                column: "ExtensionId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueCallEvent_IdempotencyKey",
                table: "QueueCallEvent",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueCallEvent_QueueCallId_EventAtUtc",
                table: "QueueCallEvent",
                columns: new[] { "QueueCallId", "EventAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueCallEvent_QueueId",
                table: "QueueCallEvent",
                column: "QueueId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueCallEvent_OrderingKey_EventAtUtc_Id",
                table: "QueueCallEvent",
                columns: new[] { "OrderingKey", "EventAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueCallEvent_ProcessingStatus_NextAttemptAtUtc",
                table: "QueueCallEvent",
                columns: new[] { "ProcessingStatus", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueCallHistory_CdrId",
                table: "QueueCallHistory",
                column: "CdrId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueCallHistory_QueueCallId",
                table: "QueueCallHistory",
                column: "QueueCallId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueCallHistory_QueueId_SegmentStartAtUtc",
                table: "QueueCallHistory",
                columns: new[] { "QueueId", "SegmentStartAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueCallHistory_PbxCallId",
                table: "QueueCallHistory",
                column: "PbxCallId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueCallHistory_SourceRecordType_CallHistoryId_SegmentId",
                table: "QueueCallHistory",
                columns: new[] { "SourceRecordType", "CallHistoryId", "SegmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueCallHistory_SourceRecordType_SegmentId_CdrId",
                table: "QueueCallHistory",
                columns: new[] { "SourceRecordType", "SegmentId", "CdrId" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueExtension_DisplayName",
                table: "QueueExtension",
                column: "DisplayName");

            migrationBuilder.CreateIndex(
                name: "IX_QueueExtension_ExtensionNumber",
                table: "QueueExtension",
                column: "ExtensionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueExtension_QueueStatus",
                table: "QueueExtension",
                column: "QueueStatus");

            migrationBuilder.CreateIndex(
                name: "IX_QueueExtension_PbxUserId",
                table: "QueueExtension",
                column: "PbxUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueOutboxMessage_PublishedAtUtc_Id",
                table: "QueueOutboxMessage",
                columns: new[] { "PublishedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueOutboxMessage_Topic_CreatedAtUtc",
                table: "QueueOutboxMessage",
                columns: new[] { "Topic", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueSchedule_QueueId_EffectiveFromDate_EffectiveToDate",
                table: "QueueSchedule",
                columns: new[] { "QueueId", "EffectiveFromDate", "EffectiveToDate" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueSchedule_QueueId_ScheduleType_IsEnabled",
                table: "QueueSchedule",
                columns: new[] { "QueueId", "ScheduleType", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueWaitingSnapshot_QueueCallId",
                table: "QueueWaitingSnapshot",
                column: "QueueCallId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueWaitingSnapshot_QueueId_CapturedAtUtc",
                table: "QueueWaitingSnapshot",
                columns: new[] { "QueueId", "CapturedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueWaitingSnapshot_QueueId_SnapshotKey_CorrelationKey",
                table: "QueueWaitingSnapshot",
                columns: new[] { "QueueId", "SnapshotKey", "CorrelationKey" },
                filter: "[CorrelationKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_QueueWaitingSnapshot_QueueId_SnapshotKey_WaitOrder",
                table: "QueueWaitingSnapshot",
                columns: new[] { "QueueId", "SnapshotKey", "WaitOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueWebhookMapping_QueueId_IsEnabled",
                table: "QueueWebhookMapping",
                columns: new[] { "QueueId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueWebhookMapping_WebhookEndpointId",
                table: "QueueWebhookMapping",
                column: "WebhookEndpointId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_EmailAddress",
                table: "Users",
                column: "EmailAddress",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_OwnedExtension",
                table: "Users",
                column: "OwnedExtension");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ThreeCxUserId",
                table: "Users",
                column: "ThreeCxUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CallCdrStatusHistory");

            migrationBuilder.DropTable(
                name: "DepartmentMemberships");

            migrationBuilder.DropTable(
                name: "XapiSyncCheckpoint");

            migrationBuilder.DropTable(
                name: "QueueAgent");

            migrationBuilder.DropTable(
                name: "QueueAgentActivity");

            migrationBuilder.DropTable(
                name: "QueueAnalyticsBucketDay");

            migrationBuilder.DropTable(
                name: "QueueAnalyticsBucketHour");

            migrationBuilder.DropTable(
                name: "QueueCallEvent");

            migrationBuilder.DropTable(
                name: "QueueCallHistory");

            migrationBuilder.DropTable(
                name: "QueueOutboxMessage");

            migrationBuilder.DropTable(
                name: "QueueSchedule");

            migrationBuilder.DropTable(
                name: "QueueSettings");

            migrationBuilder.DropTable(
                name: "QueueWaitingSnapshot");

            migrationBuilder.DropTable(
                name: "QueueWebhookMapping");

            migrationBuilder.DropTable(
                name: "CallCdrs");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "QueueCall");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "QueueExtension");

            migrationBuilder.DropTable(
                name: "Queue");
        }
    }
}
