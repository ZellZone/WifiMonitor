using Windows.Networking.Connectivity;

namespace WifiMonitor.Services;

/// <summary>
/// Reads the currently connected Wi‑Fi SSID via WinRT NetworkInformation.
/// (netsh requires Location permission on modern Windows and often fails.)
/// </summary>
public static class WifiSsidProvider
{
    public static string? GetConnectedSsid()
    {
        try
        {
            var internet = NetworkInformation.GetInternetConnectionProfile();
            var fromInternet = TryGetSsid(internet);
            if (!string.IsNullOrWhiteSpace(fromInternet))
            {
                return fromInternet;
            }

            foreach (var profile in NetworkInformation.GetConnectionProfiles())
            {
                var ssid = TryGetSsid(profile);
                if (!string.IsNullOrWhiteSpace(ssid))
                {
                    return ssid;
                }
            }
        }
        catch
        {
            // Ignore and treat as disconnected.
        }

        return null;
    }

    private static string? TryGetSsid(ConnectionProfile? profile)
    {
        if (profile is null || !profile.IsWlanConnectionProfile)
        {
            return null;
        }

        try
        {
            var ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid();
            return string.IsNullOrWhiteSpace(ssid) ? null : ssid.Trim();
        }
        catch
        {
            return null;
        }
    }
}
