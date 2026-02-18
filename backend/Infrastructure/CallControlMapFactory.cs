using CallControl.Api.Domain;

namespace CallControl.Api.Infrastructure;

public static class CallControlMapFactory
{
    public static Dictionary<string, ThreeCxDnInfoModel> ToMap(IEnumerable<ThreeCxDnInfo> fullInfo)
    {
        var map = new Dictionary<string, ThreeCxDnInfoModel>(StringComparer.Ordinal);

        foreach (var info in fullInfo)
        {
            if (string.IsNullOrWhiteSpace(info.Dn))
            {
                continue;
            }

            map[info.Dn] = new ThreeCxDnInfoModel
            {
                Dn = info.Dn,
                Type = info.Type,
                Devices = (info.Devices ?? [])
                    .Where(d => !string.IsNullOrWhiteSpace(d.DeviceId))
                    .ToDictionary(d => d.DeviceId!, d => d, StringComparer.Ordinal),
                Participants = (info.Participants ?? [])
                    .Where(p => p.Id.HasValue)
                    .ToDictionary(p => p.Id!.Value, p => p)
            };
        }

        return map;
    }
}
