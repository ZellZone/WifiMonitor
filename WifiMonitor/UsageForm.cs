using WifiMonitor.Services;

namespace WifiMonitor;

public sealed class UsageForm : Form
{
    private readonly NetworkTrafficMonitor _monitor;
    private readonly Label _todayLabel;
    private readonly Label _wifiLabel;
    private readonly ProgressBar _progress1Gb;
    private readonly ProgressBar _progress5Gb;
    private readonly Label _label1Gb;
    private readonly Label _label5Gb;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    public UsageForm(NetworkTrafficMonitor monitor)
    {
        _monitor = monitor;

        Text = "今日用量";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(320, 220);
        ShowInTaskbar = true;
        Font = new Font("Microsoft JhengHei UI", 9F);

        _todayLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 12),
            Size = new Size(288, 24),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold)
        };

        _wifiLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 40),
            Size = new Size(288, 20),
            ForeColor = Color.DimGray
        };

        _label1Gb = new Label
        {
            AutoSize = false,
            Location = new Point(16, 70),
            Size = new Size(288, 20),
            Text = "1GB 門檻"
        };

        _progress1Gb = new ProgressBar
        {
            Location = new Point(16, 92),
            Size = new Size(288, 18),
            Minimum = 0,
            Maximum = 100
        };

        _label5Gb = new Label
        {
            AutoSize = false,
            Location = new Point(16, 124),
            Size = new Size(288, 20),
            Text = "5GB 門檻"
        };

        _progress5Gb = new ProgressBar
        {
            Location = new Point(16, 146),
            Size = new Size(288, 18),
            Minimum = 0,
            Maximum = 100
        };

        var settingsBtn = new Button
        {
            Text = "設定監測 Wi‑Fi…",
            Location = new Point(16, 178),
            Size = new Size(140, 28)
        };
        settingsBtn.Click += (_, _) => WifiSettingsRequested?.Invoke(this, EventArgs.Empty);

        Controls.Add(_todayLabel);
        Controls.Add(_wifiLabel);
        Controls.Add(_label1Gb);
        Controls.Add(_progress1Gb);
        Controls.Add(_label5Gb);
        Controls.Add(_progress5Gb);
        Controls.Add(settingsBtn);

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _refreshTimer.Tick += (_, _) => RefreshUsage();
        Shown += (_, _) =>
        {
            RefreshUsage();
            _refreshTimer.Start();
        };
        FormClosed += (_, _) => _refreshTimer.Stop();
    }

    public event EventHandler? WifiSettingsRequested;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    public void RefreshUsage()
    {
        var bytes = _monitor.TodayBytes;
        _todayLabel.Text = $"今日下載：{FormatBytes(bytes)}";

        var target = _monitor.TargetDisplayName;
        if (!_monitor.HasTarget)
        {
            _wifiLabel.Text = "監測範圍：全部網路";
            _wifiLabel.ForeColor = Color.DimGray;
        }
        else if (_monitor.IsCounting)
        {
            _wifiLabel.Text = $"監測中：{target}";
            _wifiLabel.ForeColor = Color.ForestGreen;
        }
        else
        {
            string current;
            if (_monitor.TargetIsWired)
            {
                current = string.IsNullOrWhiteSpace(_monitor.CurrentSsid)
                    ? "非有線"
                    : _monitor.CurrentSsid;
            }
            else
            {
                current = string.IsNullOrWhiteSpace(_monitor.CurrentSsid) ? "未連線" : _monitor.CurrentSsid;
            }

            _wifiLabel.Text = $"暫停中（目標 {target}｜目前 {current}）";
            _wifiLabel.ForeColor = Color.DarkOrange;
        }

        _progress1Gb.Value = Percent(bytes, NetworkTrafficMonitor.Threshold1GB);
        _label1Gb.Text = $"1GB（{_progress1Gb.Value}%｜90% 前提醒）";

        _progress5Gb.Value = Percent(bytes, NetworkTrafficMonitor.Threshold5GB);
        _label5Gb.Text = $"5GB（{_progress5Gb.Value}%｜90% 前提醒）";
    }

    private static int Percent(long bytes, long threshold)
    {
        if (threshold <= 0)
        {
            return 0;
        }

        var pct = (int)Math.Min(100, bytes * 100 / threshold);
        return Math.Max(0, pct);
    }

    private static string FormatBytes(long bytes)
    {
        const double kib = 1024.0;
        const double mib = kib * 1024.0;
        const double gib = mib * 1024.0;

        if (bytes >= gib)
        {
            return $"{bytes / gib:0.00} GB";
        }

        if (bytes >= mib)
        {
            return $"{bytes / mib:0.00} MB";
        }

        if (bytes >= kib)
        {
            return $"{bytes / kib:0.00} KB";
        }

        return $"{bytes} B";
    }
}
