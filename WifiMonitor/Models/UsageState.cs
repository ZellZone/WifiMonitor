namespace WifiMonitor.Models;

public sealed class UsageState
{
    public string Date { get; set; } = string.Empty;
    public long TodayBytes { get; set; }
    public bool Notified1GB { get; set; }
    public bool Notified5GB { get; set; }

    /// <summary>
    /// When true, target network is Ethernet (wired).
    /// </summary>
    public bool TargetIsWired { get; set; }

    /// <summary>
    /// Preferred Ethernet adapter name when TargetIsWired.
    /// Empty means auto-detect.
    /// </summary>
    public string EthernetAdapterName { get; set; } = string.Empty;

    /// <summary>
    /// Only count download while connected to this SSID (when not TargetIsWired).
    /// Empty means count all networks (if no wired target either).
    /// </summary>
    public string TargetSsid { get; set; } = string.Empty;

    /// <summary>
    /// Wi-Fi to switch to when leaving the target network.
    /// </summary>
    public string SwitchSsid { get; set; } = string.Empty;

    public Dictionary<string, long> LastBytesByInterface { get; set; } = new();
}
