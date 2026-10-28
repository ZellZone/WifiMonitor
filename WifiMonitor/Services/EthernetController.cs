using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace WifiMonitor.Services;

/// <summary>
/// Enables/disables the physical Ethernet adapter (乙太網路), including when disabled.
/// Elevation (UAC) is requested only for enable/disable.
/// </summary>
public static class EthernetController
{
    private static readonly string[] PreferredNames = ["乙太網路", "Ethernet", "本地連接", "Local Area Connection"];

    private static readonly Regex InterfaceLine = new(
        @"^(?<admin>\S+)\s+(?<state>\S+)\s+(?<type>\S+)\s+(?<name>.+)$",
        RegexOptions.Compiled);

    public static IReadOnlyList<EthernetAdapterInfo> GetAdapters()
    {
        var fromNetsh = GetAdaptersFromNetsh();
        if (fromNetsh.Count > 0)
        {
            return fromNetsh;
        }

        return Array.Empty<EthernetAdapterInfo>();
    }

    public static bool IsEthernetUp(string? preferredName = null)
    {
        var adapter = FindAdapter(preferredName);
        return adapter is { IsEnabled: true, IsConnected: true };
    }

    public static bool IsEthernetEnabled(string? preferredName = null)
    {
        var adapter = FindAdapter(preferredName);
        return adapter?.IsEnabled == true;
    }

    public static string? ResolveAdapterName(string? preferredName)
    {
        var adapter = FindAdapter(preferredName);
        return adapter?.Name;
    }

    public static bool SetEnabled(string adapterName, bool enabled, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(adapterName))
        {
            error = "未指定有線網卡名稱。";
            return false;
        }

        // Prefer Chinese admin keywords used by localized netsh, then English.
        var modes = enabled
            ? new[] { "enabled", "enable" }
            : new[] { "disabled", "disable" };

        foreach (var mode in modes)
        {
            var args = $"interface set interface name=\"{EscapeQuotes(adapterName)}\" admin={mode}";
            if (RunNetshElevated(args, out var output))
            {
                return WaitForAdminState(adapterName, enabled, out error);
            }

            error = string.IsNullOrWhiteSpace(output)
                ? $"無法{(enabled ? "啟用" : "停用")}「{adapterName}」。"
                : output.Trim();
        }

        if (string.IsNullOrWhiteSpace(error))
        {
            error = $"無法{(enabled ? "啟用" : "停用")}「{adapterName}」（若取消 UAC 會失敗）。";
        }

        return false;
    }

    public static bool WaitUntilUp(string adapterName, int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (IsEthernetUp(adapterName))
            {
                return true;
            }

            Thread.Sleep(500);
        }

        return false;
    }

    private static bool WaitForAdminState(string adapterName, bool expectEnabled, out string? error)
    {
        error = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var adapter = FindAdapter(adapterName);
            if (adapter is not null && adapter.IsEnabled == expectEnabled)
            {
                if (expectEnabled)
                {
                    Thread.Sleep(1200);
                }

                return true;
            }

            Thread.Sleep(400);
        }

        // Command may have succeeded even if status parse lags.
        return true;
    }

    private static EthernetAdapterInfo? FindAdapter(string? preferredName)
    {
        var adapters = GetAdapters();
        if (adapters.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            var exact = adapters.FirstOrDefault(a =>
                string.Equals(a.Name, preferredName, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }
        }

        foreach (var preferred in PreferredNames)
        {
            var match = adapters.FirstOrDefault(a =>
                string.Equals(a.Name, preferred, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        // Prefer Intel Ethernet description when present.
        var intel = adapters.FirstOrDefault(a =>
            a.Description.Contains("Ethernet Connection", StringComparison.OrdinalIgnoreCase)
            || a.Description.Contains("I219", StringComparison.OrdinalIgnoreCase)
            || a.Description.Contains("Ethernet", StringComparison.OrdinalIgnoreCase));
        return intel ?? adapters[0];
    }

    private static List<EthernetAdapterInfo> GetAdaptersFromNetsh()
    {
        var result = new List<EthernetAdapterInfo>();
        if (!RunNetsh("interface show interface", out var output, elevated: false))
        {
            return result;
        }

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("管理狀態", StringComparison.Ordinal)
                || line.StartsWith("Admin", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            var match = InterfaceLine.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value.Trim();
            var admin = match.Groups["admin"].Value.Trim();
            var state = match.Groups["state"].Value.Trim();

            if (IsExcludedAdapter(name))
            {
                continue;
            }

            // Only keep Ethernet-like names; skip Wi-Fi.
            if (name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase)
                || name.Contains("WLAN", StringComparison.OrdinalIgnoreCase)
                || name.Contains("無線", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var enabled = IsEnabledToken(admin);
            var connected = IsConnectedToken(state);
            result.Add(new EthernetAdapterInfo(name, name, enabled, connected));
        }

        // Keep 乙太網路 / Ethernet first; drop TAP "區域連線" unless nothing else.
        var physical = result
            .Where(a => !IsTapLike(a.Name))
            .ToList();
        return physical.Count > 0 ? physical : result;
    }

    private static bool IsExcludedAdapter(string name) =>
        name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
        || name.Contains("VPN", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Loopback", StringComparison.OrdinalIgnoreCase);

    private static bool IsTapLike(string name) =>
        name.Contains("TAP", StringComparison.OrdinalIgnoreCase)
        || name.Equals("區域連線", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Local Area Connection", StringComparison.OrdinalIgnoreCase);

    private static bool IsEnabledToken(string token) =>
        token.Contains("啟用", StringComparison.OrdinalIgnoreCase)
        || token.Equals("Enabled", StringComparison.OrdinalIgnoreCase);

    private static bool IsConnectedToken(string token) =>
        token.Contains("已連線", StringComparison.OrdinalIgnoreCase)
        || token.Equals("Connected", StringComparison.OrdinalIgnoreCase);

    private static string EscapeQuotes(string value) => value.Replace("\"", "\\\"");

    private static bool RunNetshElevated(string arguments, out string output)
    {
        // First try without elevation (may work if already admin).
        if (RunNetsh(arguments, out output, elevated: false))
        {
            return true;
        }

        // Retry with UAC. Write a temp batch so Chinese names are preserved.
        try
        {
            var batPath = Path.Combine(Path.GetTempPath(), $"WifiMonitor-eth-{Guid.NewGuid():N}.cmd");
            var logPath = Path.Combine(Path.GetTempPath(), $"WifiMonitor-eth-{Guid.NewGuid():N}.log");
            var content =
                "@echo off" + Environment.NewLine
                + $"netsh {arguments} > \"{logPath}\" 2>&1" + Environment.NewLine
                + "exit /b %ERRORLEVEL%" + Environment.NewLine;
            File.WriteAllText(batPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var psi = new ProcessStartInfo
            {
                FileName = batPath,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                output = "無法啟動提權程序。";
                TryDelete(batPath);
                return false;
            }

            process.WaitForExit(30000);
            TryDelete(batPath);

            if (File.Exists(logPath))
            {
                try
                {
                    output = File.ReadAllText(logPath, Encoding.Default);
                }
                catch
                {
                    output = string.Empty;
                }

                TryDelete(logPath);
            }
            else
            {
                output = string.Empty;
            }

            if (process.ExitCode != 0)
            {
                if (string.IsNullOrWhiteSpace(output))
                {
                    output = "提權執行失敗或使用者取消 UAC。";
                }

                return false;
            }

            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            output = "已取消 UAC，無法變更乙太網路狀態。";
            return false;
        }
        catch (Exception ex)
        {
            output = ex.Message;
            return false;
        }
    }

    private static bool RunNetsh(string arguments, out string output, bool elevated)
    {
        output = string.Empty;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                RedirectStandardOutput = !elevated,
                RedirectStandardError = !elevated,
                UseShellExecute = elevated,
                Verb = elevated ? "runas" : string.Empty,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                StandardOutputEncoding = elevated ? null : Encoding.Default,
                StandardErrorEncoding = elevated ? null : Encoding.Default
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            if (!elevated)
            {
                output = process.StandardOutput.ReadToEnd();
                var err = process.StandardError.ReadToEnd();
                process.WaitForExit(15000);
                if (!string.IsNullOrWhiteSpace(err))
                {
                    output = string.IsNullOrWhiteSpace(output) ? err : $"{output}\n{err}";
                }
            }
            else
            {
                process.WaitForExit(30000);
            }

            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            output = "已取消 UAC。";
            return false;
        }
        catch (Exception ex)
        {
            output = ex.Message;
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }
}

public sealed record EthernetAdapterInfo(string Name, string Description, bool IsEnabled, bool IsConnected)
{
    public bool IsUp => IsEnabled && IsConnected;
}
