namespace pbx_demo_backend.Domain.QueueManagement.Domain;

public sealed class QueueSettingsSnapshot
{
    public int? SlaTimeSec { get; private set; }
    public int? WrapUpTimeSec { get; private set; }
    public int? RingTimeoutSec { get; private set; }
    public int? MasterTimeoutSec { get; private set; }
    public int? MaxCallersInQueue { get; private set; }
    public string? PollingStrategy { get; private set; }
    public string? RecordingMode { get; private set; }
    public bool? PriorityQueue { get; private set; }
    public bool? AnnounceQueuePosition { get; private set; }
    public int? AnnouncementIntervalSec { get; private set; }
    public bool? ResetStatisticsScheduleEnabled { get; private set; }
    public string? RawRoutesJson { get; private set; }

    public static QueueSettingsSnapshot Empty() => new();

    public QueueSettingsSnapshot WithCoreSettings(
        int? slaTimeSec,
        int? wrapUpTimeSec,
        int? ringTimeoutSec,
        int? masterTimeoutSec,
        int? maxCallersInQueue,
        string? pollingStrategy,
        string? recordingMode,
        bool? priorityQueue,
        bool? announceQueuePosition,
        int? announcementIntervalSec,
        bool? resetStatisticsScheduleEnabled,
        string? rawRoutesJson = null)
    {
        ValidateNonNegative(slaTimeSec, nameof(slaTimeSec));
        ValidateNonNegative(wrapUpTimeSec, nameof(wrapUpTimeSec));
        ValidateNonNegative(ringTimeoutSec, nameof(ringTimeoutSec));
        ValidateNonNegative(masterTimeoutSec, nameof(masterTimeoutSec));
        ValidateNonNegative(maxCallersInQueue, nameof(maxCallersInQueue));
        ValidateNonNegative(announcementIntervalSec, nameof(announcementIntervalSec));

        var next = new QueueSettingsSnapshot
        {
            SlaTimeSec = slaTimeSec,
            WrapUpTimeSec = wrapUpTimeSec,
            RingTimeoutSec = ringTimeoutSec,
            MasterTimeoutSec = masterTimeoutSec,
            MaxCallersInQueue = maxCallersInQueue,
            PollingStrategy = string.IsNullOrWhiteSpace(pollingStrategy) ? null : pollingStrategy.Trim(),
            RecordingMode = string.IsNullOrWhiteSpace(recordingMode) ? null : recordingMode.Trim(),
            PriorityQueue = priorityQueue,
            AnnounceQueuePosition = announceQueuePosition,
            AnnouncementIntervalSec = announcementIntervalSec,
            ResetStatisticsScheduleEnabled = resetStatisticsScheduleEnabled,
            RawRoutesJson = rawRoutesJson
        };

        return next;
    }

    private static void ValidateNonNegative(int? value, string name)
    {
        if (value is not null && value.Value < 0)
        {
            throw new QueueDomainValidationException($"{name} cannot be negative.");
        }
    }
}

public sealed class QueueMember
{
    public long? ExtensionId { get; private set; }
    public string ExtensionNumber { get; private set; }
    public string? DisplayName { get; private set; }
    public string? SkillGroup { get; private set; }
    public bool IsAgent { get; private set; }
    public bool IsManager { get; private set; }

    private QueueMember(
        long? extensionId,
        string extensionNumber,
        string? displayName,
        string? skillGroup,
        bool isAgent,
        bool isManager)
    {
        ExtensionId = extensionId;
        ExtensionNumber = QueueDomainGuard.Required(extensionNumber, nameof(extensionNumber));
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        SkillGroup = string.IsNullOrWhiteSpace(skillGroup) ? null : skillGroup.Trim();
        IsAgent = isAgent;
        IsManager = isManager;
    }

    public static QueueMember Agent(long? extensionId, string extensionNumber, string? displayName, string? skillGroup)
        => new(extensionId, extensionNumber, displayName, skillGroup, isAgent: true, isManager: false);

    public static QueueMember Manager(long? extensionId, string extensionNumber, string? displayName)
        => new(extensionId, extensionNumber, displayName, skillGroup: null, isAgent: false, isManager: true);

    public QueueMember MergeRoleFlags(bool isAgent, bool isManager)
        => new(ExtensionId, ExtensionNumber, DisplayName, SkillGroup, isAgent, isManager);
}

public sealed class QueueAggregate : QueueAggregateRoot
{
    private readonly Dictionary<string, QueueMember> _membersByExtensionNumber = new(StringComparer.OrdinalIgnoreCase);

    public long Id { get; private set; }
    public Guid TenantId { get; private set; }
    public int PbxQueueId { get; private set; }
    public string QueueNumber { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsDeleted { get; private set; }
    public bool? IsRegistered { get; private set; }
    public QueueSettingsSnapshot Settings { get; private set; } = QueueSettingsSnapshot.Empty();
    public IReadOnlyCollection<QueueMember> Members => _membersByExtensionNumber.Values;

    private QueueAggregate()
    {
    }

    public static QueueAggregate Create(
        long id,
        Guid tenantId,
        int pbxQueueId,
        string queueNumber,
        string name,
        DateTimeOffset occurredAtUtc)
    {
        if (pbxQueueId <= 0)
        {
            throw new QueueDomainValidationException("PbxQueueId must be greater than zero.");
        }

        var aggregate = new QueueAggregate
        {
            Id = id,
            TenantId = tenantId,
            PbxQueueId = pbxQueueId,
            QueueNumber = QueueDomainGuard.Required(queueNumber, nameof(queueNumber)),
            Name = QueueDomainGuard.Required(name, nameof(name)),
            IsDeleted = false
        };

        aggregate.Raise(new QueueCreatedDomainEvent(
            aggregate.Id,
            aggregate.TenantId,
            aggregate.PbxQueueId,
            aggregate.QueueNumber,
            aggregate.Name,
            occurredAtUtc));

        return aggregate;
    }

    public void Rename(string newName, DateTimeOffset occurredAtUtc)
    {
        var normalized = QueueDomainGuard.Required(newName, nameof(newName));
        if (string.Equals(Name, normalized, StringComparison.Ordinal))
        {
            return;
        }

        var oldName = Name;
        Name = normalized;
        Raise(new QueueRenamedDomainEvent(Id, oldName, Name, occurredAtUtc));
    }

    public void SetRegistered(bool? isRegistered)
    {
        IsRegistered = isRegistered;
    }

    public void UpdateSettings(QueueSettingsSnapshot settings, DateTimeOffset occurredAtUtc)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Raise(new QueueSettingsChangedDomainEvent(Id, occurredAtUtc));
    }

    public void ReplaceAgents(IEnumerable<QueueMember> agents, DateTimeOffset occurredAtUtc)
    {
        var incoming = (agents ?? throw new ArgumentNullException(nameof(agents))).ToList();
        ApplyMembershipReplacement(incoming, keepManagers: true);
        Raise(new QueueAgentsReplacedDomainEvent(Id, Members.Count(x => x.IsAgent), Members.Count(x => x.IsManager), occurredAtUtc));
    }

    public void ReplaceManagers(IEnumerable<QueueMember> managers, DateTimeOffset occurredAtUtc)
    {
        var incoming = (managers ?? throw new ArgumentNullException(nameof(managers))).ToList();
        ApplyMembershipReplacement(incoming, keepManagers: false);
        Raise(new QueueAgentsReplacedDomainEvent(Id, Members.Count(x => x.IsAgent), Members.Count(x => x.IsManager), occurredAtUtc));
    }

    public void MarkDeleted(DateTimeOffset occurredAtUtc)
    {
        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        Raise(new QueueDeletedDomainEvent(Id, occurredAtUtc));
    }

    private void ApplyMembershipReplacement(List<QueueMember> incoming, bool keepManagers)
    {
        foreach (var member in incoming)
        {
            QueueDomainGuard.Required(member.ExtensionNumber, nameof(member.ExtensionNumber));
        }

        if (keepManagers)
        {
            var preservedManagers = _membersByExtensionNumber.Values.Where(x => x.IsManager).ToList();
            _membersByExtensionNumber.Clear();
            foreach (var manager in preservedManagers)
            {
                _membersByExtensionNumber[manager.ExtensionNumber] = manager;
            }

            foreach (var agent in incoming.Where(x => x.IsAgent))
            {
                UpsertMember(agent);
            }
        }
        else
        {
            var preservedAgents = _membersByExtensionNumber.Values.Where(x => x.IsAgent).ToList();
            _membersByExtensionNumber.Clear();
            foreach (var agent in preservedAgents)
            {
                _membersByExtensionNumber[agent.ExtensionNumber] = agent;
            }

            foreach (var manager in incoming.Where(x => x.IsManager))
            {
                UpsertMember(manager);
            }
        }
    }

    private void UpsertMember(QueueMember incoming)
    {
        if (_membersByExtensionNumber.TryGetValue(incoming.ExtensionNumber, out var existing))
        {
            _membersByExtensionNumber[incoming.ExtensionNumber] = existing.MergeRoleFlags(
                existing.IsAgent || incoming.IsAgent,
                existing.IsManager || incoming.IsManager);
            return;
        }

        _membersByExtensionNumber[incoming.ExtensionNumber] = incoming;
    }
}
