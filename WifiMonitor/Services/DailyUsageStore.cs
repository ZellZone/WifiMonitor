using System.Text.Json;
using WifiMonitor.Models;

namespace WifiMonitor.Services;

public sealed class DailyUsageStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public DailyUsageStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WifiMonitor");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "state.json");
    }

    public UsageState Load()
    {
        UsageState state;
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                state = JsonSerializer.Deserialize<UsageState>(json, JsonOptions) ?? CreateFresh();
            }
            else
            {
                state = CreateFresh();
            }
        }
        catch
        {
            state = CreateFresh();
        }

        EnsureCurrentDay(state);
        return state;
    }

    public void Save(UsageState state)
    {
        EnsureCurrentDay(state);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    public void EnsureCurrentDay(UsageState state)
    {
        var today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        if (string.Equals(state.Date, today, StringComparison.Ordinal))
        {
            return;
        }

        state.Date = today;
        state.TodayBytes = 0;
        state.Notified1GB = false;
        state.Notified5GB = false;
    }

    private static UsageState CreateFresh() => new()
    {
        Date = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"),
        TodayBytes = 0,
        Notified1GB = false,
        Notified5GB = false,
        LastBytesByInterface = new Dictionary<string, long>()
    };
}
