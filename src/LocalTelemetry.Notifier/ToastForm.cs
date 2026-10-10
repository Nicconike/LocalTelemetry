using System.Media;
using System.Runtime.Versioning;

namespace LocalTelemetry.Notifier;

[SupportedOSPlatform("windows")]
internal sealed class ToastForm : Form
{
    private const int DisplayDurationMs = 5000;
    private const int AnimationDurationMs = 300;
    private const int FormWidth = 320;
    private const int FormHeight = 90;
    private const int FormPadding = 12;
    private const int CornerRadius = 8;

    private readonly System.Windows.Forms.Timer _closeTimer = new() { Interval = DisplayDurationMs };

    internal static bool SkipShowForTesting { get; set; }

    public new void Show()
    {
        if (SkipShowForTesting) return;
        base.Show();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_NOACTIVATE = 0x08000000;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    // Initialization
    internal ToastForm(string title, string body)
    {
        ShowInTaskbar = false;
        TopMost = true;
        FormBorderStyle = FormBorderStyle.None;
        Size = new Size(FormWidth, FormHeight);
        StartPosition = FormStartPosition.Manual;

        var screen = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(screen.Right - FormWidth - FormPadding, screen.Bottom + FormHeight);

        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(0, 0, CornerRadius * 2, CornerRadius * 2, 180, 90);
        path.AddArc(FormWidth - CornerRadius * 2, 0, CornerRadius * 2, CornerRadius * 2, 270, 90);
        path.AddArc(FormWidth - CornerRadius * 2, FormHeight - CornerRadius * 2, CornerRadius * 2, CornerRadius * 2, 0, 90);
        path.AddArc(0, FormHeight - CornerRadius * 2, CornerRadius * 2, CornerRadius * 2, 90, 90);
        path.CloseFigure();
        Region = new Region(path);

        BackColor = Color.FromArgb(28, 28, 28);

        var accentBar = new Panel
        {
            BackColor = Color.FromArgb(245, 158, 11), // Amber warning accent stripe
            Location = new Point(0, 0),
            Size = new Size(5, FormHeight),
        };
        Controls.Add(accentBar);

        var titleLabel = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(251, 191, 36), // Warning gold / amber
            Location = new Point(14, 10),
            Size = new Size(FormWidth - 26, 22),
            AutoSize = false,
        };
        Controls.Add(titleLabel);

        var bodyLabel = new Label
        {
            Text = body,
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(220, 220, 220),
            Location = new Point(14, 34),
            Size = new Size(FormWidth - 26, FormHeight - 44),
            AutoSize = false,
        };
        Controls.Add(bodyLabel);

        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            Close();
        };
        _closeTimer.Start();

        SystemSounds.Exclamation.Play();
        Load += OnLoad;
        Paint += OnPaint;
    }

    // Animation
    internal static int ComputeAnimOffset(int elapsedMs, int maxOffset, int durationMs)
    {
        double t = Math.Min(elapsedMs / (double)durationMs, 1.0);
        t = 1.0 - Math.Pow(1.0 - t, 3); // ease-out cubic
        return (int)(maxOffset * (1.0 - t));
    }

    private void OnLoad(object? sender, EventArgs e)
    {
        // Animate slide-in from below the screen edge
        var screen = Screen.PrimaryScreen!.WorkingArea;

        var animTimer = new System.Windows.Forms.Timer { Interval = 16 };
        int startTicks = Environment.TickCount;
        animTimer.Tick += (_, _) =>
        {
            int elapsed = Environment.TickCount - startTicks;
            int animOffset = ComputeAnimOffset(elapsed, FormHeight + FormPadding, AnimationDurationMs);
            Location = new Point(screen.Right - FormWidth - FormPadding, screen.Bottom - FormHeight - FormPadding + animOffset);
            if (elapsed >= AnimationDurationMs)
            {
                animTimer.Stop();
                animTimer.Dispose();
            }
        };
        animTimer.Start();
    }

    // Painting
    private void OnPaint(object? sender, PaintEventArgs e)
    {
        using var borderPen = new Pen(Color.FromArgb(60, 60, 60), 1);
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        int r = CornerRadius;
        path.AddArc(0, 0, r * 2, r * 2, 180, 90);
        path.AddArc(FormWidth - r * 2 - 1, 0, r * 2, r * 2, 270, 90);
        path.AddArc(FormWidth - r * 2 - 1, FormHeight - r * 2 - 1, r * 2, r * 2, 0, 90);
        path.AddArc(0, FormHeight - r * 2 - 1, r * 2, r * 2, 90, 90);
        path.CloseFigure();
        e.Graphics.DrawPath(borderPen, path);
    }

    // Disposal
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _closeTimer.Dispose();
        base.Dispose(disposing);
    }
}
