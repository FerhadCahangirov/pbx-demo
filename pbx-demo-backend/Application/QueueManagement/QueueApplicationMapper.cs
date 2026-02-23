using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CallControl.Api.Domain;
using pbx_demo_backend.Domain.QueueManagement.Contracts;

namespace CallControl.Api.Application.QueueManagement;

public sealed class QueueApplicationMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null
    };

    public QueuePagedResult<QueueDto> ToPagedQueueDto(
        IReadOnlyList<QueueEntity> queues,
        int? totalCount,
        IReadOnlyDictionary<long, List<QueueAgentEntity>> membershipsByQueueId,
        IReadOnlyDictionary<long, ExtensionEntity> extensionsById)
    {
        return new QueuePagedResult<QueueDto>
        {
            TotalCount = totalCount,
            Items = queues
                .Select(q => ToQueueDto(
                    q,
                    membershipsByQueueId.TryGetValue(q.Id, out var memberships) ? memberships : [],
                    extensionsById))
                .ToList()
        };
    }

    public QueueDto ToQueueDto(
        QueueEntity queue,
        IReadOnlyList<QueueAgentEntity> memberships,
        IReadOnlyDictionary<long, ExtensionEntity> extensionsById)
    {
        ArgumentNullException.ThrowIfNull(queue);

        var activeMemberships = (memberships ?? [])
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.AgentNumberSnapshot, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id)
            .ToList();

        var agents = new List<QueueAgentAssignmentDto>();
        var managers = new List<QueueManagerAssignmentDto>();

        foreach (var membership in activeMemberships)
        {
            extensionsById.TryGetValue(membership.ExtensionId, out var extension);
            var extensionNumber = extension?.ExtensionNumber
                ?? membership.AgentNumberSnapshot
                ?? string.Empty;
            var displayName = extension?.DisplayName ?? membership.AgentNameSnapshot;

            if (membership.IsAgentMember)
            {
                agents.Add(new QueueAgentAssignmentDto
                {
                    ExtensionId = membership.ExtensionId,
                    ExtensionNumber = extensionNumber,
                    DisplayName = displayName,
                    SkillGroup = membership.SkillGroup
                });
            }

            if (membership.IsQueueManager)
            {
                managers.Add(new QueueManagerAssignmentDto
                {
                    ExtensionId = membership.ExtensionId,
                    ExtensionNumber = extensionNumber,
                    DisplayName = displayName
                });
            }
        }

        return new QueueDto
        {
            Id = queue.Id,
            PbxQueueId = queue.PbxQueueId,
            QueueNumber = queue.QueueNumber,
            Name = queue.Name,
            IsRegistered = queue.IsRegistered,
            Settings = ToSettingsDto(queue.Settings),
            Agents = agents,
            Managers = managers
        };
    }

    public QueueSettingsDto ToSettingsDto(QueueSettingsEntity? settings)
    {
        if (settings is null)
        {
            return new QueueSettingsDto();
        }

        return new QueueSettingsDto
        {
            AgentAvailabilityMode = settings.AgentAvailabilityMode,
            AnnouncementIntervalSec = settings.AnnouncementIntervalSec,
            AnnounceQueuePosition = settings.AnnounceQueuePosition,
            CallbackEnableTimeSec = settings.CallbackEnableTimeSec,
            CallbackPrefix = settings.CallbackPrefix,
            EnableIntro = settings.EnableIntro,
            GreetingFile = settings.GreetingFile,
            IntroFile = settings.IntroFile,
            OnHoldFile = settings.OnHoldFile,
            PromptSet = settings.PromptSet,
            PlayFullPrompt = settings.PlayFullPrompt,
            PriorityQueue = settings.PriorityQueue,
            RingTimeoutSec = settings.RingTimeoutSec,
            MasterTimeoutSec = settings.MasterTimeoutSec,
            MaxCallersInQueue = settings.MaxCallersInQueue,
            SlaTimeSec = settings.SlaTimeSec,
            WrapUpTimeSec = settings.WrapUpTimeSec,
            PollingStrategy = settings.PollingStrategy,
            RecordingMode = settings.RecordingMode,
            NotifyCodes = DeserializeStringList(settings.NotifyCodesJson),
            ResetStatisticsScheduleEnabled = settings.ResetStatisticsScheduleEnabled,
            ResetQueueStatisticsSchedule = new QueueResetStatisticsScheduleDto
            {
                Frequency = settings.ResetStatsFrequency,
                DayOfWeek = settings.ResetStatsDayOfWeek,
                Time = settings.ResetStatsTime
            },
            BreakRoute = DeserializeRoute(settings.BreakRouteJson),
            HolidaysRoute = DeserializeRoute(settings.HolidaysRouteJson),
            OutOfOfficeRoute = DeserializeRoute(settings.OutOfOfficeRouteJson),
            ForwardNoAnswer = DeserializeDestination(settings.ForwardNoAnswerJson)
        };
    }

    public XapiPbxQueueDto ToXapiCreateQueue(CreateQueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var xapi = new XapiPbxQueueDto
        {
            Number = Required(request.Number, nameof(request.Number)),
            Name = Required(request.Name, nameof(request.Name))
        };

        ApplySettingsToXapi(xapi, request.Settings);

        var agents = MapAgentsToXapi(request.Agents);
        if (agents.Count > 0)
        {
            xapi.Agents = agents;
        }

        var managers = MapManagersToXapi(request.Managers);
        if (managers.Count > 0)
        {
            xapi.Managers = managers;
        }

        return xapi;
    }

    public XapiPbxQueueDto MergeUpdateIntoXapi(XapiPbxQueueDto current, UpdateQueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            current.Name = request.Name.Trim();
        }

        if (request.Settings is not null)
        {
            ApplySettingsToXapi(current, request.Settings);
        }

        if (request.Agents is not null && request.ReplaceAgents)
        {
            current.Agents = MapAgentsToXapi(request.Agents);
        }

        if (request.Managers is not null && request.ReplaceManagers)
        {
            current.Managers = MapManagersToXapi(request.Managers);
        }

        return current;
    }

    public void ApplyXapiQueueToEntity(QueueEntity entity, XapiPbxQueueDto xapi, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(xapi);

        entity.PbxQueueId = xapi.Id;
        entity.QueueNumber = xapi.Number?.Trim() ?? entity.QueueNumber;
        entity.Name = xapi.Name?.Trim() ?? entity.Name;
        entity.IsRegistered = xapi.IsRegistered;
        entity.IsDeleted = false;
        entity.LastXapiSyncAtUtc = nowUtc;
        entity.RawJson = Serialize(xapi);
        entity.LastXapiHash = ComputeSha256(entity.RawJson);
        entity.UpdatedAtUtc = nowUtc;

        entity.Settings ??= new QueueSettingsEntity
        {
            Queue = entity,
            QueueId = entity.Id
        };

        ApplyXapiSettingsToEntity(entity.Settings, xapi, nowUtc);
    }

    public void ApplyXapiSettingsToEntity(QueueSettingsEntity entity, XapiPbxQueueDto xapi, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(xapi);

        entity.AgentAvailabilityMode = xapi.AgentAvailabilityMode;
        entity.AnnouncementIntervalSec = xapi.AnnouncementInterval;
        entity.AnnounceQueuePosition = xapi.AnnounceQueuePosition;
        entity.CallbackEnableTimeSec = xapi.CallbackEnableTime;
        entity.CallbackPrefix = Trim(xapi.CallbackPrefix);
        entity.EnableIntro = xapi.EnableIntro;
        entity.GreetingFile = Trim(xapi.GreetingFile);
        entity.IntroFile = Trim(xapi.IntroFile);
        entity.OnHoldFile = Trim(xapi.OnHoldFile);
        entity.PromptSet = Trim(xapi.PromptSet);
        entity.PlayFullPrompt = xapi.PlayFullPrompt;
        entity.PriorityQueue = xapi.PriorityQueue;
        entity.RingTimeoutSec = xapi.RingTimeout;
        entity.MasterTimeoutSec = xapi.MasterTimeout;
        entity.MaxCallersInQueue = xapi.MaxCallersInQueue;
        entity.SlaTimeSec = xapi.SLATime;
        entity.WrapUpTimeSec = xapi.WrapUpTime;
        entity.PollingStrategy = xapi.PollingStrategy?.ToString();
        entity.RecordingMode = xapi.Recording?.ToString();
        entity.NotifyCodesJson = Serialize(xapi.NotifyCodes?.Select(x => x.ToString()).ToList() ?? []);
        entity.ResetStatisticsScheduleEnabled = xapi.ResetStatisticsScheduleEnabled;
        entity.ResetStatsFrequency = xapi.ResetQueueStatisticsSchedule?.Frequency?.ToString();
        entity.ResetStatsDayOfWeek = xapi.ResetQueueStatisticsSchedule?.Day?.ToString();
        entity.ResetStatsTime = Trim(xapi.ResetQueueStatisticsSchedule?.Time);
        entity.BreakRouteJson = SerializeRoute(xapi.BreakRoute);
        entity.HolidaysRouteJson = SerializeRoute(xapi.HolidaysRoute);
        entity.OutOfOfficeRouteJson = SerializeRoute(xapi.OutOfOfficeRoute);
        entity.ForwardNoAnswerJson = SerializeDestination(xapi.ForwardNoAnswer);
        entity.TranscriptionMode = xapi.TranscriptionMode?.ToString();
        entity.ChatOwnershipType = xapi.TypeOfChatOwnershipType?.ToString();
        entity.CallUsEnableChat = xapi.CallUsEnableChat;
        entity.CallUsEnablePhone = xapi.CallUsEnablePhone;
        entity.CallUsEnableVideo = xapi.CallUsEnableVideo;
        entity.CallUsRequirement = xapi.CallUsRequirement?.ToString();
        entity.ClickToCallId = Trim(xapi.ClickToCallId);
        entity.UpdatedAtUtc = nowUtc;
    }

    public List<XapiPbxQueueAgentDto> MapAgentsToXapi(IEnumerable<QueueAgentAssignmentDto>? agents)
        => (agents ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.ExtensionNumber))
            .Select(x => new XapiPbxQueueAgentDto
            {
                Id = x.ExtensionId is > 0 ? checked((int)x.ExtensionId.Value) : null,
                Number = x.ExtensionNumber.Trim(),
                Name = Trim(x.DisplayName),
                SkillGroup = Trim(x.SkillGroup)
            })
            .ToList();

    public List<XapiPbxQueueManagerDto> MapManagersToXapi(IEnumerable<QueueManagerAssignmentDto>? managers)
        => (managers ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.ExtensionNumber))
            .Select(x => new XapiPbxQueueManagerDto
            {
                Id = x.ExtensionId is > 0 ? checked((int)x.ExtensionId.Value) : null,
                Number = x.ExtensionNumber.Trim(),
                Name = Trim(x.DisplayName)
            })
            .ToList();

    private void ApplySettingsToXapi(XapiPbxQueueDto xapi, QueueSettingsDto? settings)
    {
        if (settings is null)
        {
            return;
        }

        xapi.AgentAvailabilityMode = settings.AgentAvailabilityMode;
        xapi.AnnouncementInterval = settings.AnnouncementIntervalSec;
        xapi.AnnounceQueuePosition = settings.AnnounceQueuePosition;
        xapi.CallbackEnableTime = settings.CallbackEnableTimeSec;
        xapi.CallbackPrefix = Trim(settings.CallbackPrefix);
        xapi.EnableIntro = settings.EnableIntro;
        xapi.GreetingFile = Trim(settings.GreetingFile);
        xapi.IntroFile = Trim(settings.IntroFile);
        xapi.OnHoldFile = Trim(settings.OnHoldFile);
        xapi.PromptSet = Trim(settings.PromptSet);
        xapi.PlayFullPrompt = settings.PlayFullPrompt;
        xapi.PriorityQueue = settings.PriorityQueue;
        xapi.RingTimeout = settings.RingTimeoutSec;
        xapi.MasterTimeout = settings.MasterTimeoutSec;
        xapi.MaxCallersInQueue = settings.MaxCallersInQueue;
        xapi.SLATime = settings.SlaTimeSec;
        xapi.WrapUpTime = settings.WrapUpTimeSec;
        xapi.PollingStrategy = ParseEnumOrNull<XapiPbxPollingStrategyType>(settings.PollingStrategy, nameof(settings.PollingStrategy));
        xapi.Recording = ParseEnumOrNull<XapiPbxQueueRecording>(settings.RecordingMode, nameof(settings.RecordingMode));
        xapi.NotifyCodes = MapNotifyCodes(settings.NotifyCodes);
        xapi.ResetStatisticsScheduleEnabled = settings.ResetStatisticsScheduleEnabled;
        xapi.ResetQueueStatisticsSchedule = settings.ResetQueueStatisticsSchedule is null
            ? xapi.ResetQueueStatisticsSchedule
            : new XapiPbxResetQueueStatisticsScheduleDto
            {
                Frequency = ParseEnumOrNull<XapiPbxResetQueueStatisticsFrequency>(settings.ResetQueueStatisticsSchedule.Frequency, "ResetQueueStatisticsSchedule.Frequency"),
                Day = ParseEnumOrNull<XapiPbxDayOfWeek>(settings.ResetQueueStatisticsSchedule.DayOfWeek, "ResetQueueStatisticsSchedule.DayOfWeek"),
                Time = Trim(settings.ResetQueueStatisticsSchedule.Time)
            };

        xapi.BreakRoute = MapRouteToXapi(settings.BreakRoute);
        xapi.HolidaysRoute = MapRouteToXapi(settings.HolidaysRoute);
        xapi.OutOfOfficeRoute = MapRouteToXapi(settings.OutOfOfficeRoute);
        xapi.ForwardNoAnswer = MapDestinationToXapi(settings.ForwardNoAnswer);
    }

    private static List<XapiPbxQueueNotifyCode> MapNotifyCodes(IEnumerable<string>? values)
    {
        var result = new List<XapiPbxQueueNotifyCode>();
        foreach (var value in values ?? [])
        {
            var parsed = ParseEnumOrNull<XapiPbxQueueNotifyCode>(value, "NotifyCodes");
            if (parsed is not null)
            {
                result.Add(parsed.Value);
            }
        }

        return result;
    }

    private static XapiPbxRouteDto? MapRouteToXapi(QueueRouteDto? route)
        => route is null ? null : new XapiPbxRouteDto
        {
            IsPromptEnabled = route.IsPromptEnabled,
            Prompt = Trim(route.Prompt),
            Route = MapDestinationToXapi(route.Route)
        };

    private static XapiPbxDestinationDto? MapDestinationToXapi(QueueDestinationDto? destination)
        => destination is null ? null : new XapiPbxDestinationDto
        {
            External = Trim(destination.External),
            Name = Trim(destination.Name),
            Number = Trim(destination.Number),
            Tags = MapUserTags(destination.Tags),
            To = ParseEnumOrNull<XapiPbxDestinationType>(destination.To, nameof(destination.To)),
            Type = ParseEnumOrNull<XapiPbxPeerType>(destination.Type, nameof(destination.Type))
        };

    private static List<XapiPbxUserTag>? MapUserTags(IEnumerable<string>? values)
    {
        var result = new List<XapiPbxUserTag>();
        foreach (var value in values ?? [])
        {
            var parsed = ParseEnumOrNull<XapiPbxUserTag>(value, "Tags");
            if (parsed is not null)
            {
                result.Add(parsed.Value);
            }
        }

        return result.Count == 0 ? null : result;
    }

    private static QueueRouteDto? DeserializeRoute(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<QueueRouteDto>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static QueueDestinationDto? DeserializeDestination(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<QueueDestinationDto>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static List<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string? SerializeRoute(XapiPbxRouteDto? route)
        => route is null ? null : Serialize(new QueueRouteDto
        {
            IsPromptEnabled = route.IsPromptEnabled,
            Prompt = route.Prompt,
            Route = route.Route is null ? null : new QueueDestinationDto
            {
                External = route.Route.External,
                Name = route.Route.Name,
                Number = route.Route.Number,
                Tags = route.Route.Tags?.Select(x => x.ToString()).ToList(),
                To = route.Route.To?.ToString(),
                Type = route.Route.Type?.ToString()
            }
        });

    private static string? SerializeDestination(XapiPbxDestinationDto? destination)
        => destination is null ? null : Serialize(new QueueDestinationDto
        {
            External = destination.External,
            Name = destination.Name,
            Number = destination.Number,
            Tags = destination.Tags?.Select(x => x.ToString()).ToList(),
            To = destination.To?.ToString(),
            Type = destination.Type?.ToString()
        });

    private static TEnum? ParseEnumOrNull<TEnum>(string? value, string fieldName)
        where TEnum : struct, Enum
    {
        var normalized = Trim(value);
        if (normalized is null)
        {
            return null;
        }

        if (Enum.TryParse<TEnum>(normalized, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new BadRequestException($"Invalid value '{value}' for {fieldName}.");
    }

    private static byte[] ComputeSha256(string payload)
        => SHA256.HashData(Encoding.UTF8.GetBytes(payload));

    private static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, JsonOptions);

    private static string Required(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BadRequestException($"{fieldName} is required.");
        }

        return value.Trim();
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

