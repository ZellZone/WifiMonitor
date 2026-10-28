using System.Net.NetworkInformation;
using WifiMonitor.Models;

namespace WifiMonitor.Services;

public sealed class NetworkTrafficMonitor
{
    public const long Threshold1GB = 1L << 30;
    public const long Threshold5GB = 5L << 30;
    public const double NotifyRatio = 0.9;

    /// <summary>Notify when today reaches 90% of 1GB.</summary>
    public static long Notify1GB => Threshold1GB * 9 / 10;

    /// <summary>Notify when today reaches 90% of 5GB.</summary>
    public static long Notify5GB => Threshold5GB * 9 / 10;

    private readonly DailyUsageStore _store;
    private UsageState _state;
    private bool _initialized;

    public NetworkTrafficMonitor(DailyUsageStore store)
    {
        _store = store;
        _state = store.Load();
    }

    public long TodayBytes
    {
        get
        {
            _store.EnsureCurrentDay(_state);
            return _state.TodayBytes;
        }
    }

    public UsageState State => _state;

    public bool TargetIsWired => _state.TargetIsWired;

    public string EthernetAdapterName => _state.EthernetAdapterName ?? string.Empty;

    public string TargetSsid => _state.TargetSsid ?? string.Empty;

    public string SwitchSsid => _state.SwitchSsid ?? string.Empty;

    public string? CurrentSsid { get; private set; }

    public bool IsEthernetConnected { get; private set; }

    /// <summary>True when traffic is being counted this poll cycle.</summary>
    public bool IsCounting { get; private set; }

    public bool HasTarget => TargetIsWired || !string.IsNullOrWhiteSpace(TargetSsid);

    public bool CanSwitchWifi =>
        !string.IsNullOrWhiteSpace(SwitchSsid)
        && (TargetIsWired || !string.IsNullOrWhiteSpace(TargetSsid));

    public string TargetDisplayName =>
        TargetIsWired
            ? (string.IsNullOrWhiteSpace(EthernetAdapterName) ? "有線網路" : EthernetAdapterName)
            : TargetSsid;

    public void SetWifiSettings(
        bool targetIsWired,
        string? ethernetAdapterName,
        string? targetSsid,
        string? switchSsid)
    {
        _state.TargetIsWired = targetIsWired;
        _state.EthernetAdapterName = (ethernetAdapterName ?? string.Empty).Trim();
        _state.TargetSsid = targetIsWired ? string.Empty : (targetSsid ?? string.Empty).Trim();
        _state.SwitchSsid = (switchSsid ?? string.Empty).Trim();
        _store.Save(_state);
    }

    /// <summary>
    /// Poll interfaces, accumulate today's download delta, and return newly crossed thresholds.
    /// </summary>
    public IReadOnlyList<long> Poll()
    {
        _store.EnsureCurrentDay(_state);

        CurrentSsid = WifiSsidProvider.GetConnectedSsid();
        IsEthernetConnected = EthernetController.IsEthernetUp(
            TargetIsWired ? EthernetAdapterName : null);
        IsCounting = ShouldCount();

        var current = ReadInterfaceBytes(GetCountMode());
        long delta = 0;

        if (!_initialized)
        {
            foreach (var (id, bytes) in current)
            {
                _state.LastBytesByInterface[id] = bytes;
            }

            RemoveMissingInterfaces(current);
            _initialized = true;
            _store.Save(_state);
            return Array.Empty<long>();
        }

        foreach (var (id, bytes) in current)
        {
            if (_state.LastBytesByInterface.TryGetValue(id, out var last))
            {
                if (bytes >= last)
                {
                    delta += bytes - last;
                }
                else
                {
                    delta += bytes;
                }
            }

            _state.LastBytesByInterface[id] = bytes;
        }

        RemoveMissingInterfaces(current);

        if (IsCounting && delta > 0)
        {
            _state.TodayBytes += delta;
        }

        var crossed = new List<long>();
        if (!_state.Notified1GB && _state.TodayBytes >= Notify1GB)
        {
            _state.Notified1GB = true;
            crossed.Add(Notify1GB);
        }

        if (!_state.Notified5GB && _state.TodayBytes >= Notify5GB)
        {
            _state.Notified5GB = true;
            crossed.Add(Notify5GB);
        }

        _store.Save(_state);
        return crossed;
    }

    private CountMode GetCountMode()
    {
        if (TargetIsWired)
        {
            return CountMode.EthernetOnly;
        }

        if (!string.IsNullOrWhiteSpace(TargetSsid))
        {
            return CountMode.WirelessOnly;
        }

        return CountMode.All;
    }

    private bool ShouldCount()
    {
        if (TargetIsWired)
        {
            return IsEthernetConnected;
        }

        if (string.IsNullOrWhiteSpace(TargetSsid))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(CurrentSsid)
               && string.Equals(CurrentSsid, TargetSsid.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void RemoveMissingInterfaces(Dictionary<string, long> current)
    {
        var stale = _state.LastBytesByInterface.Keys
            .Where(id => !current.ContainsKey(id))
            .ToList();
        foreach (var id in stale)
        {
            _state.LastBytesByInterface.Remove(id);
        }
    }

    private Dictionary<string, long> ReadInterfaceBytes(CountMode mode)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (mode == CountMode.WirelessOnly
                && nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
            {
                continue;
            }

            if (mode == CountMode.EthernetOnly)
            {
                if (nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet
                    && nic.NetworkInterfaceType != NetworkInterfaceType.GigabitEthernet)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(EthernetAdapterName)
                    && !string.Equals(nic.Name, EthernetAdapterName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            try
            {
                var stats = nic.GetIPv4Statistics();
                result[nic.Id] = stats.BytesReceived;
            }
            catch (NetworkInformationException)
            {
                // Interface may not support IPv4 stats.
            }
        }

        return result;
    }

    private enum CountMode
    {
        All,
        WirelessOnly,
        EthernetOnly
    }
}
