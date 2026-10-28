namespace WifiMonitor.Services;

/// <summary>
/// Switches between target network (Wi-Fi or Ethernet) and switch Wi-Fi.
/// Wired mode only toggles 乙太網路 on/off (with UAC if needed).
/// </summary>
public static class NetworkSwitcher
{
    public static bool SwitchAwayFromTarget(
        bool targetIsWired,
        string? ethernetAdapterName,
        string switchSsid,
        out string? error)
    {
        error = null;

        if (targetIsWired)
        {
            var adapter = EthernetController.ResolveAdapterName(ethernetAdapterName)
                          ?? "乙太網路";

            // Only disable Ethernet — same as Settings > 乙太網路 > 停用.
            if (!EthernetController.SetEnabled(adapter, enabled: false, out error))
            {
                return false;
            }

            Thread.Sleep(600);

            // If already on switch Wi-Fi, done; otherwise connect.
            var current = WifiSsidProvider.GetConnectedSsid();
            if (string.Equals(current, switchSsid, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return WifiConnector.ConnectToSsid(switchSsid, out error);
        }

        return WifiConnector.ConnectToSsid(switchSsid, out error);
    }

    public static bool SwitchBackToTarget(
        bool targetIsWired,
        string? ethernetAdapterName,
        string targetSsid,
        out string? error)
    {
        error = null;

        if (targetIsWired)
        {
            var adapter = EthernetController.ResolveAdapterName(ethernetAdapterName)
                          ?? "乙太網路";

            // Only enable Ethernet — do not touch Wi-Fi.
            if (!EthernetController.SetEnabled(adapter, enabled: true, out error))
            {
                return false;
            }

            // Cable may need a moment; don't fail hard if link is slow.
            EthernetController.WaitUntilUp(adapter, timeoutMs: 10000);
            return true;
        }

        return WifiConnector.ConnectToSsid(targetSsid, out error);
    }
}
