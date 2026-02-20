namespace CallControl.Api.Infrastructure;

public readonly record struct EntityOperation(string Dn, string Type, string Id);

public static class EntityPathHelper
{
    public static EntityOperation DetermineOperation(string entity)
    {
        if (string.IsNullOrWhiteSpace(entity))
        {
            return new EntityOperation(string.Empty, string.Empty, string.Empty);
        }

        var parts = entity.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 4 || !parts[0].Equals("callcontrol", StringComparison.OrdinalIgnoreCase))
        {
            return new EntityOperation(string.Empty, string.Empty, string.Empty);
        }

        // Expected format: /callcontrol/{dn}/{entityType}/{entityId}
        return new EntityOperation(parts[1], parts[2], parts[3]);
    }
}
