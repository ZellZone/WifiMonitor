using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace WifiMonitor.Services;

/// <summary>
/// Connects to a saved Wi-Fi profile via netsh.
/// </summary>
public static class WifiConnector
{
    private const int MaxAttempts = 3;
    private const int ScanWaitMs = 2500;
    private const int ConnectVerifyTimeoutMs = 12000;

    private static readonly Regex ProfileNameLine = new(
        @"^\s*(?:所有使用者設定檔|All User Profile)\s*:\s*(.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InterfaceNameLine = new(
        @"^\s*(?:名稱|Name)\s*:\s*(.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool ConnectToSsid(string ssid, out string? error)
    {
        error = null;
        var target = ssid.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            error = "未指定 Wi-Fi 名稱。";
            return false;
        }

        var profileName = FindProfileName(target);
        if (profileName is null)
        {
            error = $"找不到已儲存的 Wi-Fi 設定檔：{target}";
            return false;
        }

        var iface = GetWifiInterfaceName();
        string? lastOutput = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            Disconnect(iface);
            ScanNetworks(iface);

            var connectArgs = BuildConnectArgs(profileName, iface);
            if (!RunNetsh(connectArgs, out var output))
            {
                lastOutput = output;
                Thread.Sleep(1000);
                continue;
            }

            if (WaitForConnection(target))
            {
                return true;
            }

            lastOutput = $"第 {attempt} 次連線後未偵測到 {target}。";
            Thread.Sleep(1000);
        }

        error = string.IsNullOrWhiteSpace(lastOutput) ? "連線失敗。" : lastOutput.Trim();
        return false;
    }

    public static IReadOnlyList<string> GetSavedProfileNames()
    {
        if (!RunNetsh("wlan show profiles", out var output))
        {
            return Array.Empty<string>();
        }

        var profiles = new List<string>();
        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = ProfileNameLine.Match(rawLine);
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                profiles.Add(name);
            }
        }

        return profiles;
    }

    private static string BuildConnectArgs(string profileName, string? iface)
    {
        var args = $"wlan connect name={Quote(profileName)}";
        if (!string.IsNullOrWhiteSpace(iface))
        {
            args += $" interface={Quote(iface)}";
        }

        return args;
    }

    public static void DisconnectWifi()
    {
        Disconnect(GetWifiInterfaceName());
    }

    private static void Disconnect(string? iface)
    {
        if (!string.IsNullOrWhiteSpace(iface))
        {
            RunNetsh($"wlan disconnect interface={Quote(iface)}", out _);
        }
        else
        {
            RunNetsh("wlan disconnect", out _);
        }

        Thread.Sleep(400);
    }

    private static void ScanNetworks(string? iface)
    {
        if (!string.IsNullOrWhiteSpace(iface))
        {
            RunNetsh($"wlan show networks interface={Quote(iface)} mode=bssid", out _);
        }
        else
        {
            RunNetsh("wlan show networks mode=bssid", out _);
        }

        Thread.Sleep(ScanWaitMs);
    }

    private static bool WaitForConnection(string expectedSsid)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ConnectVerifyTimeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var current = WifiSsidProvider.GetConnectedSsid();
            if (string.Equals(current, expectedSsid, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Thread.Sleep(500);
        }

        return false;
    }

    private static string? GetWifiInterfaceName()
    {
        if (!RunNetsh("wlan show interfaces", out var output))
        {
            return null;
        }

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = InterfaceNameLine.Match(rawLine.Trim());
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return "Wi-Fi";
    }

    private static string? FindProfileName(string ssid)
    {
        foreach (var profile in GetSavedProfileNames())
        {
            if (string.Equals(profile, ssid, StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        if (ProfileExists(ssid))
        {
            return ssid;
        }

        return null;
    }

    private static bool ProfileExists(string profileName)
    {
        var args = $"wlan show profile name={Quote(profileName)}";
        return RunNetsh(args, out var output)
               && !output.Contains("找不到", StringComparison.OrdinalIgnoreCase)
               && !output.Contains("not found", StringComparison.OrdinalIgnoreCase)
               && !output.Contains("找不到設定檔", StringComparison.OrdinalIgnoreCase);
    }

    private static string Quote(string value)
    {
        if (value.Contains('"'))
        {
            value = value.Replace("\"", "\\\"");
        }

        return $"\"{value}\"";
    }

    private static bool RunNetsh(string arguments, out string output)
    {
        output = string.Empty;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            output = process.StandardOutput.ReadToEnd();
            var err = process.StandardError.ReadToEnd();
            process.WaitForExit(15000);

            if (!string.IsNullOrWhiteSpace(err))
            {
                output = string.IsNullOrWhiteSpace(output) ? err : $"{output}\n{err}";
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            output = ex.Message;
            return false;
        }
    }
}
