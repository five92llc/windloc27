using QRCoder;

namespace WinLoc27;

/// <summary>
/// Shown while locked. Input is swallowed by the hooks, so this form is display-only:
/// the unlock QR code, a clock and a hint about the other unlock methods.
/// </summary>
sealed class LockOverlay : Form
{
    readonly Label _clock = new();
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };

    protected override bool ShowWithoutActivation => true;

    public LockOverlay(bool fullScreen, string? url, string hint)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(14, 14, 18);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 11f);

        if (fullScreen)
        {
            Bounds = SystemInformation.VirtualScreen;
        }
        else
        {
            var area = Screen.PrimaryScreen!.WorkingArea;
            Size = new Size(320, 440);
            Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);
        }

        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            BackColor = Color.Transparent,
        };

        var title = new Label { Text = "Locked", AutoSize = true, Font = new Font("Segoe UI", fullScreen ? 28f : 18f, FontStyle.Bold), Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 0, 10) };
        panel.Controls.Add(title);

        if (url != null)
        {
            var side = fullScreen ? 280 : 220;
            panel.Controls.Add(new PictureBox
            {
                Image = RenderQr(url),
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(side, side),
                Anchor = AnchorStyles.None,
                Margin = new Padding(0, 0, 0, 10),
            });
            panel.Controls.Add(new Label { Text = "Scan with your phone, then enter your password", AutoSize = true, Anchor = AnchorStyles.None });
        }

        if (hint.Length > 0)
            panel.Controls.Add(new Label { Text = hint, AutoSize = true, ForeColor = Color.Silver, Anchor = AnchorStyles.None, Margin = new Padding(0, 8, 0, 0), TextAlign = ContentAlignment.MiddleCenter });

        _clock.AutoSize = true;
        _clock.Anchor = AnchorStyles.None;
        _clock.ForeColor = Color.Gray;
        _clock.Margin = new Padding(0, 12, 0, 0);
        panel.Controls.Add(_clock);

        Controls.Add(panel);
        Layout += (_, _) => panel.Location = new Point((ClientSize.Width - panel.Width) / 2, (ClientSize.Height - panel.Height) / 2);

        UpdateClock();
        _tick.Tick += (_, _) =>
        {
            UpdateClock();
            TopMost = false; // re-assert above other topmost windows
            TopMost = true;
        };
        _tick.Start();
    }

    void UpdateClock() => _clock.Text = DateTime.Now.ToString("ddd d MMM  HH:mm");

    static Bitmap RenderQr(string text)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(10);
        using var ms = new MemoryStream(png);
        return new Bitmap(ms);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Cursor.Hide();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Cursor.Show();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tick.Dispose();
        base.Dispose(disposing);
    }
}
