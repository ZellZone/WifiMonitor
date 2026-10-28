using Microsoft.Toolkit.Uwp.Notifications;

namespace WifiMonitor.Services;

public sealed class ToastNotifier
{
    public void NotifyThreshold(long notifyBytes)
    {
        var limitLabel = notifyBytes switch
        {
            _ when notifyBytes == NetworkTrafficMonitor.Notify1GB => "1GB",
            _ when notifyBytes == NetworkTrafficMonitor.Notify5GB => "5GB",
            _ => FormatBytes(notifyBytes)
        };

        new ToastContentBuilder()
            .AddText("WifiMonitor")
            .AddText($"今日網路下載量已接近 {limitLabel}（達 90%）")
            .Show();
    }

    private static string FormatBytes(long bytes)
    {
        const double gib = 1L << 30;
        return $"{bytes / gib:0.##}GB";
    }
}
