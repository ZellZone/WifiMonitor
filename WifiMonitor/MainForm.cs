using WifiMonitor.Services;

namespace WifiMonitor;

public sealed class MainForm : Form
{
    private readonly NetworkTrafficMonitor _monitor;
    private readonly ToastNotifier _toast;
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _trayMenu;
    private readonly ToolStripMenuItem _toggleFloatItem;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private UsageForm? _usageForm;
    private FloatingWidgetForm? _floatWidget;

    public MainForm()
    {
        var store = new DailyUsageStore();
        _monitor = new NetworkTrafficMonitor(store);
        _toast = new ToastNotifier();

        // Hidden host form for the tray app.
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        Opacity = 0;
        Size = new Size(0, 0);
        Text = "WifiMonitor";

        _toggleFloatItem = new ToolStripMenuItem("隱藏浮動視窗", null, (_, _) => ToggleFloatingWidget());

        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add("今日用量", null, (_, _) => ShowUsageWindow());
        _trayMenu.Items.Add("Wi‑Fi 設定…", null, (_, _) => ShowWifiSettings());
        _trayMenu.Items.Add(_toggleFloatItem);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("結束", null, (_, _) => ExitApp());

        _trayIcon = new NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Visible = true,
            Text = "WifiMonitor",
            ContextMenuStrip = _trayMenu
        };
        _trayIcon.DoubleClick += (_, _) => ShowUsageWindow();

        _pollTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _pollTimer.Tick += (_, _) => OnPoll();
        _pollTimer.Start();

        // Seed baselines immediately.
        OnPoll();
        ShowFloatingWidget();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Hide();
    }

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

    private void OnPoll()
    {
        try
        {
            var crossed = _monitor.Poll();
            foreach (var threshold in crossed)
            {
                _toast.NotifyThreshold(threshold);
            }

            UpdateTrayTooltip();
            _usageForm?.RefreshUsage();
            _floatWidget?.RefreshUsage();
        }
        catch
        {
            // Keep tray alive even if a poll fails.
        }
    }

    private void UpdateTrayTooltip()
    {
        string text;
        if (_monitor.HasTarget && !_monitor.IsCounting)
        {
            text = $"暫停｜{_monitor.TargetDisplayName}｜{FormatBytes(_monitor.TodayBytes)}";
        }
        else if (_monitor.HasTarget)
        {
            text = $"{_monitor.TargetDisplayName}｜{FormatBytes(_monitor.TodayBytes)}";
        }
        else
        {
            text = $"今日下載：{FormatBytes(_monitor.TodayBytes)}";
        }

        // NotifyIcon.Text max length is 63 characters.
        if (text.Length > 63)
        {
            text = text[..63];
        }

        _trayIcon.Text = text;
    }

    private void ShowUsageWindow()
    {
        if (_usageForm is null || _usageForm.IsDisposed)
        {
            _usageForm = new UsageForm(_monitor);
            _usageForm.WifiSettingsRequested += (_, _) => ShowWifiSettings();
        }

        _usageForm.RefreshUsage();
        if (!_usageForm.Visible)
        {
            _usageForm.Show();
        }

        _usageForm.BringToFront();
        _usageForm.Activate();
    }

    private void ShowWifiSettings()
    {
        using var form = new WifiSettingsForm(_monitor);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            UpdateTrayTooltip();
            _usageForm?.RefreshUsage();
            _floatWidget?.RefreshUsage();
        }
    }

    private void ShowFloatingWidget()
    {
        if (_floatWidget is null || _floatWidget.IsDisposed)
        {
            _floatWidget = new FloatingWidgetForm(_monitor);
            _floatWidget.DetailRequested += (_, _) => ShowUsageWindow();
        }

        _floatWidget.RefreshUsage();
        if (!_floatWidget.Visible)
        {
            _floatWidget.Show(this);
        }

        _toggleFloatItem.Text = "隱藏浮動視窗";
    }

    private void HideFloatingWidget()
    {
        _floatWidget?.Hide();
        _toggleFloatItem.Text = "顯示浮動視窗";
    }

    private void ToggleFloatingWidget()
    {
        if (_floatWidget is { Visible: true })
        {
            HideFloatingWidget();
        }
        else
        {
            ShowFloatingWidget();
        }
    }

    private void ExitApp()
    {
        _pollTimer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayMenu.Dispose();
        _usageForm?.Dispose();
        _floatWidget?.Dispose();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollTimer.Dispose();
            _trayIcon.Dispose();
            _trayMenu.Dispose();
            _usageForm?.Dispose();
            _floatWidget?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Icon CreateTrayIcon()
    {
        var resourcePath = Path.Combine(AppContext.BaseDirectory, "Resources", "tray.ico");
        if (File.Exists(resourcePath))
        {
            return new Icon(resourcePath);
        }

        // Fallback: generate a simple circular icon.
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Color.FromArgb(0, 120, 215));
            g.FillEllipse(brush, 1, 1, 13, 13);
            using var pen = new Pen(Color.White, 1.5f);
            g.DrawEllipse(pen, 4, 4, 7, 7);
        }

        var hIcon = bmp.GetHicon();
        using var temp = Icon.FromHandle(hIcon);
        return (Icon)temp.Clone();
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
