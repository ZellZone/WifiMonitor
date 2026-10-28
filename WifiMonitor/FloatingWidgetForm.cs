using WifiMonitor.Services;

namespace WifiMonitor;

/// <summary>
/// Compact always-on-top widget near the taskbar showing today's download.
/// </summary>
public sealed class FloatingWidgetForm : Form
{
    private readonly Label _titleLabel;
    private readonly Label _valueLabel;
    private readonly Button _switchBtn;
    private readonly NetworkTrafficMonitor _monitor;
    private Point _dragOffset;
    private bool _dragging;

    public FloatingWidgetForm(NetworkTrafficMonitor monitor)
    {
        _monitor = monitor;

        Text = "WifiMonitor";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(148, 58);
        BackColor = Color.FromArgb(32, 36, 42);
        Font = new Font("Microsoft JhengHei UI", 8F);
        Padding = new Padding(0);

        _switchBtn = new Button
        {
            Text = "切換",
            Location = new Point(104, 4),
            Size = new Size(38, 20),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(52, 58, 68),
            ForeColor = Color.FromArgb(220, 224, 230),
            Font = new Font("Microsoft JhengHei UI", 8F),
            Cursor = Cursors.Hand,
            TabStop = false
        };
        _switchBtn.FlatAppearance.BorderColor = Color.FromArgb(90, 98, 110);
        _switchBtn.FlatAppearance.BorderSize = 1;
        _switchBtn.Click += (_, _) => OnSwitchClicked();

        _titleLabel = new Label
        {
            AutoSize = false,
            Text = "今日下載",
            ForeColor = Color.FromArgb(180, 186, 194),
            Location = new Point(8, 6),
            Size = new Size(92, 16),
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.SizeAll
        };

        _valueLabel = new Label
        {
            AutoSize = false,
            Text = "0 B",
            ForeColor = Color.White,
            Font = new Font("Microsoft JhengHei UI", 11F, FontStyle.Bold),
            Location = new Point(8, 24),
            Size = new Size(132, 26),
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.SizeAll
        };

        Controls.Add(_switchBtn);
        Controls.Add(_titleLabel);
        Controls.Add(_valueLabel);

        AttachDragHandlers(this);
        AttachDragHandlers(_titleLabel);
        AttachDragHandlers(_valueLabel);

        DoubleClick += (_, _) => OnWidgetDoubleClick();
        _titleLabel.DoubleClick += (_, _) => OnWidgetDoubleClick();
        _valueLabel.DoubleClick += (_, _) => OnWidgetDoubleClick();

        PlaceNearTaskbar();
        RefreshUsage();
    }

    public event EventHandler? DetailRequested;

    public void RefreshUsage()
    {
        if (IsDisposed)
        {
            return;
        }

        _valueLabel.Text = FormatBytes(_monitor.TodayBytes);
        _switchBtn.Enabled = _monitor.CanSwitchWifi;

        if (!_monitor.HasTarget)
        {
            _titleLabel.Text = "今日下載";
            _titleLabel.ForeColor = Color.FromArgb(180, 186, 194);
            _valueLabel.ForeColor = Color.White;
        }
        else if (_monitor.IsCounting)
        {
            _titleLabel.Text = _monitor.TargetIsWired ? "監測中（有線）" : "監測中";
            _titleLabel.ForeColor = Color.FromArgb(140, 210, 160);
            _valueLabel.ForeColor = Color.White;
        }
        else
        {
            _titleLabel.Text = "已暫停";
            _titleLabel.ForeColor = Color.FromArgb(230, 170, 90);
            _valueLabel.ForeColor = Color.FromArgb(200, 200, 200);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(70, 78, 90));
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
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

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // WS_EX_TOOLWINDOW: keep out of Alt+Tab.
            cp.ExStyle |= 0x00000080;
            return cp;
        }
    }

    private async void OnSwitchClicked()
    {
        if (!_monitor.CanSwitchWifi)
        {
            MessageBox.Show(this, "請先在設定中指定目標網路與切換 Wi‑Fi。", "WifiMonitor",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var leaveTarget = _monitor.IsCounting;

        _switchBtn.Enabled = false;
        _switchBtn.Text = "切換中";
        try
        {
            string? error = null;
            var ok = await Task.Run(() =>
            {
                if (leaveTarget)
                {
                    return NetworkSwitcher.SwitchAwayFromTarget(
                        _monitor.TargetIsWired,
                        _monitor.EthernetAdapterName,
                        _monitor.SwitchSsid,
                        out error);
                }

                return NetworkSwitcher.SwitchBackToTarget(
                    _monitor.TargetIsWired,
                    _monitor.EthernetAdapterName,
                    _monitor.TargetSsid,
                    out error);
            });

            if (!ok)
            {
                MessageBox.Show(this, error ?? "切換失敗。", "WifiMonitor",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _switchBtn.Text = "切換";
            RefreshUsage();
        }
    }

    private void PlaceNearTaskbar()
    {
        var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        var area = screen.WorkingArea;
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
    }

    private void AttachDragHandlers(Control control)
    {
        control.MouseDown += OnDragMouseDown;
        control.MouseMove += OnDragMouseMove;
        control.MouseUp += OnDragMouseUp;
    }

    private void OnDragMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = true;
        _dragOffset = e.Location;
        if (sender is Control c && c != this)
        {
            _dragOffset = new Point(e.X + c.Left, e.Y + c.Top);
        }
    }

    private void OnDragMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging || e.Button != MouseButtons.Left)
        {
            return;
        }

        var screenPoint = PointToScreen(e.Location);
        if (sender is Control c && c != this)
        {
            screenPoint = c.PointToScreen(e.Location);
        }

        Location = new Point(screenPoint.X - _dragOffset.X, screenPoint.Y - _dragOffset.Y);
    }

    private void OnDragMouseUp(object? sender, MouseEventArgs e)
    {
        _dragging = false;
    }

    private void OnWidgetDoubleClick()
    {
        DetailRequested?.Invoke(this, EventArgs.Empty);
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
