using WifiMonitor.Services;

namespace WifiMonitor;

public sealed class WifiSettingsForm : Form
{
    private readonly NetworkTrafficMonitor _monitor;
    private readonly RadioButton _wiredRadio;
    private readonly RadioButton _wifiRadio;
    private readonly ComboBox _ethernetBox;
    private readonly TextBox _targetBox;
    private readonly TextBox _switchBox;
    private readonly Label _currentLabel;
    private readonly Label _targetLabel;
    private readonly Button _targetCurrentBtn;
    private readonly Label _ethernetLabel;

    public WifiSettingsForm(NetworkTrafficMonitor monitor)
    {
        _monitor = monitor;

        Text = "網路設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(400, 300);
        Font = new Font("Microsoft JhengHei UI", 9F);
        ShowInTaskbar = true;

        var hint = new Label
        {
            AutoSize = false,
            Location = new Point(16, 12),
            Size = new Size(368, 36),
            Text = "目標網路：只在此網路下累計下載量。\n切換 Wi‑Fi：離開目標時連上此 Wi‑Fi；再按一次切回目標。"
        };

        _currentLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 52),
            Size = new Size(368, 20),
            Text = "目前連線："
        };

        var typeLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 80),
            Size = new Size(80, 20),
            Text = "目標類型"
        };

        _wiredRadio = new RadioButton
        {
            Text = "有線",
            Location = new Point(96, 78),
            AutoSize = true,
            Checked = monitor.TargetIsWired
        };
        _wifiRadio = new RadioButton
        {
            Text = "Wi‑Fi",
            Location = new Point(160, 78),
            AutoSize = true,
            Checked = !monitor.TargetIsWired
        };
        _wiredRadio.CheckedChanged += (_, _) => UpdateTargetModeUi();
        _wifiRadio.CheckedChanged += (_, _) => UpdateTargetModeUi();

        _ethernetLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 112),
            Size = new Size(80, 20),
            Text = "有線網卡"
        };

        _ethernetBox = new ComboBox
        {
            Location = new Point(96, 108),
            Size = new Size(268, 24),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var adapter in EthernetController.GetAdapters())
        {
            var text = adapter.IsUp
                ? $"{adapter.Name}（連線中）"
                : adapter.IsEnabled
                    ? $"{adapter.Name}（已啟用）"
                    : $"{adapter.Name}（已停用）";
            _ethernetBox.Items.Add(new AdapterItem(adapter.Name, text));
        }

        if (_ethernetBox.Items.Count == 0)
        {
            _ethernetBox.Items.Add(new AdapterItem(string.Empty, "（未偵測到有線網卡）"));
        }

        SelectEthernet(monitor.EthernetAdapterName);

        _targetLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 112),
            Size = new Size(80, 20),
            Text = "目標 Wi‑Fi"
        };

        _targetBox = new TextBox
        {
            Location = new Point(96, 108),
            Size = new Size(180, 24),
            Text = monitor.TargetSsid
        };

        _targetCurrentBtn = new Button
        {
            Text = "使用目前",
            Location = new Point(284, 106),
            Size = new Size(80, 28)
        };
        _targetCurrentBtn.Click += (_, _) => FillCurrentWifi(_targetBox);

        var switchLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 152),
            Size = new Size(80, 20),
            Text = "切換 Wi‑Fi"
        };

        _switchBox = new TextBox
        {
            Location = new Point(96, 148),
            Size = new Size(180, 24),
            Text = monitor.SwitchSsid
        };

        var switchCurrentBtn = new Button
        {
            Text = "使用目前",
            Location = new Point(284, 146),
            Size = new Size(80, 28)
        };
        switchCurrentBtn.Click += (_, _) => FillCurrentWifi(_switchBox);

        var note = new Label
        {
            AutoSize = false,
            Location = new Point(16, 186),
            Size = new Size(368, 36),
            ForeColor = Color.DimGray,
            Text = "有線 ↔ Wi‑Fi 時只會啟停「乙太網路」。首次可能跳出 UAC，請按允許。"
        };

        var saveBtn = new Button
        {
            Text = "儲存",
            Location = new Point(204, 248),
            Size = new Size(90, 28)
        };
        saveBtn.Click += (_, _) =>
        {
            var wired = _wiredRadio.Checked;
            var ethernetName = (_ethernetBox.SelectedItem as AdapterItem)?.Name ?? string.Empty;
            if (wired && string.IsNullOrWhiteSpace(ethernetName))
            {
                MessageBox.Show(this, "請選擇有線網卡。", "WifiMonitor",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!wired && string.IsNullOrWhiteSpace(_targetBox.Text))
            {
                // Allow empty target = monitor all; still OK.
            }

            if (string.IsNullOrWhiteSpace(_switchBox.Text))
            {
                MessageBox.Show(this, "請填寫切換 Wi‑Fi（用來來回切換）。", "WifiMonitor",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _monitor.SetWifiSettings(wired, ethernetName, _targetBox.Text, _switchBox.Text);
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancelBtn = new Button
        {
            Text = "取消",
            Location = new Point(294, 248),
            Size = new Size(90, 28),
            DialogResult = DialogResult.Cancel
        };

        Controls.Add(hint);
        Controls.Add(_currentLabel);
        Controls.Add(typeLabel);
        Controls.Add(_wiredRadio);
        Controls.Add(_wifiRadio);
        Controls.Add(_ethernetLabel);
        Controls.Add(_ethernetBox);
        Controls.Add(_targetLabel);
        Controls.Add(_targetBox);
        Controls.Add(_targetCurrentBtn);
        Controls.Add(switchLabel);
        Controls.Add(_switchBox);
        Controls.Add(switchCurrentBtn);
        Controls.Add(note);
        Controls.Add(saveBtn);
        Controls.Add(cancelBtn);
        AcceptButton = saveBtn;
        CancelButton = cancelBtn;

        UpdateTargetModeUi();
        RefreshCurrent();
    }

    private void UpdateTargetModeUi()
    {
        var wired = _wiredRadio.Checked;
        _ethernetLabel.Visible = wired;
        _ethernetBox.Visible = wired;
        _targetLabel.Visible = !wired;
        _targetBox.Visible = !wired;
        _targetCurrentBtn.Visible = !wired;
    }

    private void SelectEthernet(string preferredName)
    {
        if (_ethernetBox.Items.Count == 0)
        {
            return;
        }

        for (var i = 0; i < _ethernetBox.Items.Count; i++)
        {
            if (_ethernetBox.Items[i] is AdapterItem item
                && string.Equals(item.Name, preferredName, StringComparison.OrdinalIgnoreCase))
            {
                _ethernetBox.SelectedIndex = i;
                return;
            }
        }

        _ethernetBox.SelectedIndex = 0;
    }

    private void FillCurrentWifi(TextBox box)
    {
        var ssid = WifiSsidProvider.GetConnectedSsid();
        if (string.IsNullOrWhiteSpace(ssid))
        {
            MessageBox.Show(this, "目前沒有偵測到 Wi‑Fi 連線。", "WifiMonitor",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        box.Text = ssid;
    }

    private void RefreshCurrent()
    {
        var wifi = WifiSsidProvider.GetConnectedSsid();
        var wired = EthernetController.IsEthernetUp();
        if (!string.IsNullOrWhiteSpace(wifi) && wired)
        {
            _currentLabel.Text = $"目前連線：有線 + Wi‑Fi（{wifi}）";
        }
        else if (wired)
        {
            _currentLabel.Text = "目前連線：有線網路";
        }
        else if (!string.IsNullOrWhiteSpace(wifi))
        {
            _currentLabel.Text = $"目前連線：{wifi}";
        }
        else
        {
            _currentLabel.Text = "目前連線：（未連線）";
        }
    }

    private sealed class AdapterItem
    {
        public AdapterItem(string name, string display)
        {
            Name = name;
            Display = display;
        }

        public string Name { get; }
        public string Display { get; }
        public override string ToString() => Display;
    }
}
