namespace WinLoc27;

sealed class SettingsForm : Form
{
    readonly AppSettings _s;

    readonly TextBox _pw = new() { UseSystemPasswordChar = true, Width = 220 };
    readonly TextBox _pw2 = new() { UseSystemPasswordChar = true, Width = 220 };
    readonly CheckBox _qr = new() { Text = "Unlock with QR code + password (phone on same Wi-Fi)", AutoSize = true };
    readonly NumericUpDown _port = new() { Minimum = 1024, Maximum = 65535, Width = 80 };
    readonly CheckBox _bt = new() { Text = "Unlock when a paired Bluetooth device connects", AutoSize = true };
    readonly ComboBox _devices = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    readonly CheckBox _combo = new() { Text = "Unlock with secret key combination", AutoSize = true };
    readonly Button _record = new() { Text = "Record combination", AutoSize = true };
    readonly Label _comboInfo = new() { AutoSize = true };
    readonly NumericUpDown _delay = new() { Minimum = 0, Maximum = 120, Width = 60 };
    readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    readonly CheckBox _awake = new() { Text = "Keep the PC and screen awake", AutoSize = true };
    readonly NumericUpDown _auto = new() { Minimum = 0, Maximum = 1440, Width = 70 };
    readonly CheckBox _updates = new() { Text = "Check GitHub for updates on start", AutoSize = true };

    List<(string Id, string Name)> _paired = new();

    // Combo recording
    bool _recording;
    readonly HashSet<int> _held = new();
    readonly HashSet<int> _captured = new();
    List<int>? _newCombo;

    public SettingsForm(AppSettings settings)
    {
        _s = settings;
        Text = "WinLoc27 settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14);
        Font = new Font("Segoe UI", 9.5f);

        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };

        flow.Controls.Add(Section("Password (required for the QR method)"));
        flow.Controls.Add(Row("New password", _pw));
        flow.Controls.Add(Row("Repeat", _pw2));
        if (_s.HasPassword) flow.Controls.Add(Hint("A password is set. Leave blank to keep it."));

        flow.Controls.Add(Section("QR code"));
        flow.Controls.Add(_qr);
        flow.Controls.Add(Row("Port", _port));

        flow.Controls.Add(Section("Bluetooth"));
        flow.Controls.Add(_bt);
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        refresh.Click += async (_, _) => await LoadDevices();
        flow.Controls.Add(Row("Device", _devices, refresh));
        flow.Controls.Add(Hint("Pair your phone in Windows Settings first. It unlocks when the phone connects to the PC."));

        flow.Controls.Add(Section("Secret key combination"));
        flow.Controls.Add(_combo);
        flow.Controls.Add(Row("Combination", _record, _comboInfo));
        flow.Controls.Add(Hint("Hold 3 or more keys at once while recording, then release them."));

        flow.Controls.Add(Section("General"));
        flow.Controls.Add(Row("Lock after (seconds)", _delay));
        _mode.Items.AddRange(new object[] { "Full screen", "Small corner panel" });
        flow.Controls.Add(Row("Lock screen", _mode));
        flow.Controls.Add(_awake);
        flow.Controls.Add(Row("Auto-unlock after (min, 0 = never)", _auto));
        flow.Controls.Add(_updates);

        var ok = new Button { Text = "Save", DialogResult = DialogResult.None, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += (_, _) => Save();
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Width = 440, Margin = new Padding(0, 14, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        flow.Controls.Add(buttons);
        CancelButton = cancel;

        Controls.Add(flow);

        _record.Click += (_, _) => StartRecording();
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;

        Load += async (_, _) =>
        {
            _qr.Checked = _s.QrEnabled;
            _port.Value = Math.Clamp(_s.Port, 1024, 65535);
            _bt.Checked = _s.BluetoothEnabled;
            _combo.Checked = _s.ComboEnabled;
            _delay.Value = Math.Clamp(_s.LockDelaySeconds, 0, 120);
            _mode.SelectedIndex = _s.FullScreenOverlay ? 0 : 1;
            _awake.Checked = _s.KeepAwake;
            _auto.Value = Math.Clamp(_s.AutoUnlockMinutes, 0, 1440);
            _updates.Checked = _s.AutoCheckUpdates;
            ShowComboInfo();
            await LoadDevices();
        };
    }

    async Task LoadDevices()
    {
        _devices.Items.Clear();
        try { _paired = await BluetoothWatcher.ListPairedAsync(); }
        catch { _paired = new(); }
        foreach (var d in _paired) _devices.Items.Add(d.Name);
        var idx = _paired.FindIndex(d => d.Id == _s.BluetoothDeviceId);
        if (idx >= 0) _devices.SelectedIndex = idx;
        else if (_paired.Count > 0 && _devices.SelectedIndex < 0) _devices.SelectedIndex = 0;
    }

    void ShowComboInfo()
    {
        _comboInfo.Text = _newCombo != null
            ? "New: " + Describe(_newCombo)
            : _s.HasCombo ? $"Set ({_s.ComboLength} keys)" : "Not set";
    }

    void StartRecording()
    {
        _recording = true;
        _held.Clear();
        _captured.Clear();
        _comboInfo.Text = "Press and hold your keys…";
        _record.Enabled = false;
        ActiveControl = null;
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = e.SuppressKeyPress = true;
        var vk = InputBlocker.Normalize((int)e.KeyCode);
        _held.Add(vk);
        _captured.Add(vk);
        _comboInfo.Text = Describe(_captured);
    }

    void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;
        _held.Remove(InputBlocker.Normalize((int)e.KeyCode));
        if (_held.Count > 0) return;

        _recording = false;
        _record.Enabled = true;
        if (_captured.Count < 3)
        {
            MessageBox.Show(this, "Use at least 3 keys held together.", "Too short", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _newCombo = null;
        }
        else
        {
            _newCombo = _captured.OrderBy(k => k).ToList();
        }
        ShowComboInfo();
    }

    static string Describe(IEnumerable<int> vks) => string.Join(" + ", vks.OrderBy(k => k).Select(k => ((Keys)k).ToString()));

    void Save()
    {
        if (_pw.Text.Length > 0 || _pw2.Text.Length > 0)
        {
            if (_pw.Text != _pw2.Text) { Warn("The passwords do not match."); return; }
            if (_pw.Text.Length < 6) { Warn("Use at least 6 characters."); return; }
            _s.SetPassword(_pw.Text);
        }
        if (_qr.Checked && !_s.HasPassword) { Warn("Set a password to use the QR method."); return; }
        if (_bt.Checked && _devices.SelectedIndex < 0) { Warn("Pick a paired Bluetooth device."); return; }
        if (_combo.Checked && !_s.HasCombo && _newCombo == null) { Warn("Record a key combination first."); return; }
        if (!_qr.Checked && !_bt.Checked && !_combo.Checked) { Warn("Enable at least one unlock method or you could not unlock."); return; }

        if (_newCombo != null) _s.SetCombo(_newCombo);
        _s.QrEnabled = _qr.Checked;
        _s.Port = (int)_port.Value;
        _s.BluetoothEnabled = _bt.Checked;
        if (_devices.SelectedIndex >= 0)
        {
            _s.BluetoothDeviceId = _paired[_devices.SelectedIndex].Id;
            _s.BluetoothDeviceName = _paired[_devices.SelectedIndex].Name;
        }
        _s.ComboEnabled = _combo.Checked;
        _s.LockDelaySeconds = (int)_delay.Value;
        _s.FullScreenOverlay = _mode.SelectedIndex == 0;
        _s.KeepAwake = _awake.Checked;
        _s.AutoUnlockMinutes = (int)_auto.Value;
        _s.AutoCheckUpdates = _updates.Checked;
        _s.Save();
        DialogResult = DialogResult.OK;
        Close();
    }

    void Warn(string text) => MessageBox.Show(this, text, "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    static Label Section(string text) => new() { Text = text, AutoSize = true, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Margin = new Padding(0, 12, 0, 4) };
    static Label Hint(string text) => new() { Text = text, AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(440, 0) };

    static FlowLayoutPanel Row(string label, params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        row.Controls.Add(new Label { Text = label, AutoSize = false, Width = 150, TextAlign = ContentAlignment.MiddleLeft, Height = 26 });
        row.Controls.AddRange(controls);
        return row;
    }
}
