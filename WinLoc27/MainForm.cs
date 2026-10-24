namespace WinLoc27;

sealed class MainForm : Form
{
    readonly AppSettings _settings = AppSettings.Load();
    readonly InputBlocker _blocker = new();
    readonly BluetoothWatcher _bluetooth = new();
    readonly UnlockServer _server;
    readonly System.Windows.Forms.Timer _countdown = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer _autoUnlock = new();

    readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(340, 0) };
    readonly Button _lockButton = new() { Text = "Lock now", Height = 44, Width = 340, Font = new Font("Segoe UI", 12f, FontStyle.Bold) };
    readonly Button _settingsButton = new() { Text = "Settings", Width = 340, Height = 32 };
    readonly Button _updateButton = new() { Text = "Check for updates", Width = 340, Height = 32 };
    readonly Label _version = new() { Text = Updater.CurrentText, AutoSize = true, ForeColor = Color.Gray };

    LockOverlay? _overlay;
    Updater.Release? _pendingUpdate;
    int _remaining;

    public bool IsLocked { get; private set; }

    public MainForm()
    {
        Text = "WinLoc27 " + Updater.CurrentText;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        Font = new Font("Segoe UI", 10f);

        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        flow.Controls.Add(new Label { Text = "WinLoc27", Font = new Font("Segoe UI", 18f, FontStyle.Bold), AutoSize = true });
        flow.Controls.Add(_status);
        flow.Controls.Add(_lockButton);
        flow.Controls.Add(_settingsButton);
        flow.Controls.Add(_updateButton);
        flow.Controls.Add(_version);
        Controls.Add(flow);

        _server = new UnlockServer(
            pw => _settings.VerifyPassword(pw),
            () => BeginInvoke((Action)Unlock));

        _blocker.ComboCheck = held => _settings.MatchesCombo(held);
        _blocker.ComboMatched += () => Unlock();
        _bluetooth.DeviceConnected += () => Unlock();

        _lockButton.Click += (_, _) => { if (_countdown.Enabled) CancelCountdown(); else BeginLock(); };
        _settingsButton.Click += (_, _) => OpenSettings();
        _updateButton.Click += async (_, _) => await OnUpdateClicked();
        _countdown.Tick += (_, _) => { if (--_remaining <= 0) EngageLock(); else ShowCountdown(); };
        _autoUnlock.Tick += (_, _) => Unlock();

        ApplyKeepAwake();
        RefreshStatus();

        Shown += async (_, _) =>
        {
            if (!_settings.HasPassword && !_settings.HasCombo) OpenSettings();
            if (_settings.AutoCheckUpdates) await CheckForUpdates(silent: true);
        };
    }

    // ---------------------------------------------------------------- updates

    async Task OnUpdateClicked()
    {
        if (_pendingUpdate == null) { await CheckForUpdates(silent: false); return; }

        _updateButton.Enabled = _lockButton.Enabled = false;
        _updateButton.Text = "Downloading…";
        try
        {
            await Updater.StageAndRestartAsync(_pendingUpdate);
            Application.Exit();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Update failed: " + ex.Message, "WinLoc27", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _updateButton.Text = $"Update to v{_pendingUpdate.Version} and restart";
            _updateButton.Enabled = _lockButton.Enabled = true;
        }
    }

    async Task CheckForUpdates(bool silent)
    {
        if (IsLocked) return;
        _updateButton.Enabled = false;
        try
        {
            var release = await Updater.CheckAsync();
            _pendingUpdate = release;
            if (release != null)
                _updateButton.Text = $"Update to v{release.Version} and restart";
            else
            {
                _updateButton.Text = "Check for updates";
                if (!silent)
                    MessageBox.Show(this, $"You are up to date ({Updater.CurrentText}).", "WinLoc27", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            if (!silent)
                MessageBox.Show(this, "Could not check for updates: " + ex.Message, "WinLoc27", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _updateButton.Enabled = true;
        }
    }

    // ---------------------------------------------------------------- lock / unlock

    void BeginLock()
    {
        if (!HasAnyMethod())
        {
            MessageBox.Show(this, "Set up at least one unlock method in Settings first, otherwise you could not unlock.",
                "WinLoc27", MessageBoxButtons.OK, MessageBoxIcon.Information);
            OpenSettings();
            return;
        }
        if (_settings.LockDelaySeconds <= 0) { EngageLock(); return; }
        _remaining = _settings.LockDelaySeconds;
        _lockButton.Text = "Cancel";
        _settingsButton.Enabled = false;
        ShowCountdown();
        _countdown.Start();
    }

    void CancelCountdown()
    {
        _countdown.Stop();
        _lockButton.Text = "Lock now";
        _settingsButton.Enabled = true;
        RefreshStatus();
    }

    void ShowCountdown() => _status.Text = $"Locking in {_remaining}…";

    bool HasAnyMethod() =>
        (_settings.QrEnabled && _settings.HasPassword) ||
        (_settings.ComboEnabled && _settings.HasCombo) ||
        (_settings.BluetoothEnabled && _settings.BluetoothDeviceId != null);

    async void EngageLock()
    {
        _countdown.Stop();
        _lockButton.Text = "Lock now";
        _settingsButton.Enabled = true;

        // Bring up the unlock channels first; only lock if at least one really works.
        var working = new List<string>();
        string? url = null;
        var problems = new List<string>();

        if (_settings.ComboEnabled && _settings.HasCombo) working.Add("secret key combination");

        if (_settings.QrEnabled && _settings.HasPassword)
        {
            try
            {
                _server.Start(_settings.Port);
                _server.Arm();
                url = _server.GetUrl();
                if (url != null) working.Add("QR code");
                else problems.Add("QR: no network address found.");
            }
            catch (Exception ex)
            {
                _server.Stop();
                problems.Add($"QR: could not listen on port {_settings.Port} ({ex.Message}).");
            }
        }

        if (_settings.BluetoothEnabled && _settings.BluetoothDeviceId != null)
        {
            if (await _bluetooth.StartAsync(_settings.BluetoothDeviceId))
                working.Add($"Bluetooth ({_settings.BluetoothDeviceName})");
            else
                problems.Add("Bluetooth: could not open the selected device.");
        }

        if (working.Count == 0)
        {
            _server.Stop();
            MessageBox.Show(this, "Not locking, no unlock method is available:\n\n" + string.Join("\n", problems),
                "WinLoc27", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            RefreshStatus();
            return;
        }

        try
        {
            _blocker.Start();
        }
        catch (Exception ex)
        {
            _server.Stop();
            _bluetooth.Stop();
            MessageBox.Show(this, ex.Message, "WinLoc27", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        IsLocked = true;
        var hint = new List<string>();
        if (_settings.BluetoothEnabled && working.Any(w => w.StartsWith("Bluetooth")))
            hint.Add($"{_settings.BluetoothDeviceName} unlocks when it connects");
        if (_settings.ComboEnabled && _settings.HasCombo) hint.Add("or enter your secret key combination");
        _overlay = new LockOverlay(_settings.FullScreenOverlay, url, string.Join("\n", hint));
        _overlay.Show();
        Hide();

        _bluetooth.Arm();
        if (_settings.AutoUnlockMinutes > 0)
        {
            _autoUnlock.Interval = _settings.AutoUnlockMinutes * 60_000;
            _autoUnlock.Start();
        }
    }

    void Unlock()
    {
        if (!IsLocked) return;
        IsLocked = false;
        _blocker.Stop();
        _server.Stop();
        _bluetooth.Stop();
        _autoUnlock.Stop();
        _overlay?.Close();
        _overlay?.Dispose();
        _overlay = null;
        Show();
        Activate();
        RefreshStatus();
    }

    // ---------------------------------------------------------------- settings / awake

    void OpenSettings()
    {
        using var form = new SettingsForm(_settings);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            ApplyKeepAwake();
            RefreshStatus();
        }
    }

    void ApplyKeepAwake()
    {
        Native.SetThreadExecutionState(_settings.KeepAwake
            ? Native.ES_CONTINUOUS | Native.ES_SYSTEM_REQUIRED | Native.ES_DISPLAY_REQUIRED
            : Native.ES_CONTINUOUS);
    }

    void RefreshStatus()
    {
        var methods = new List<string>();
        if (_settings.QrEnabled && _settings.HasPassword) methods.Add("QR + password");
        if (_settings.BluetoothEnabled && _settings.BluetoothDeviceName != null) methods.Add("Bluetooth: " + _settings.BluetoothDeviceName);
        if (_settings.ComboEnabled && _settings.HasCombo) methods.Add("key combination");
        _status.Text = (methods.Count == 0 ? "No unlock method set up yet." : "Unlock with: " + string.Join(", ", methods)) +
                       (_settings.KeepAwake ? "\nThe PC will stay awake." : "");
    }

    // ---------------------------------------------------------------- lifetime

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (IsLocked) { e.Cancel = true; return; }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Native.SetThreadExecutionState(Native.ES_CONTINUOUS);
        _blocker.Dispose();
        _server.Dispose();
        _bluetooth.Dispose();
        base.OnFormClosed(e);
    }
}
