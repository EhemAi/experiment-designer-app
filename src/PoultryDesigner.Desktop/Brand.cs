using System.Drawing.Drawing2D;

namespace PoultryDesigner.Desktop;

internal static class Brand
{
    private static string Asset(string file) => Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", file);
    internal static readonly Icon Icon = new(Asset("experiment-designer.ico"));
    internal static readonly Image Logo = Image.FromFile(Asset("experiment-designer.png"));
}

internal sealed class SplashForm : Form
{
    internal const int MinimumDisplayMilliseconds = 5000;
    internal readonly LaunchArtwork Loading = new() { Dock = DockStyle.Fill };
    internal SplashForm()
    {
        Text = "Experiment Designer"; Icon = Brand.Icon; BackColor = Theme.Canvas;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new(600, 380); Controls.Add(Loading);
    }
}

// One measured composition avoids separate label scaling and overlap at high DPI.
internal sealed class LaunchArtwork : Control
{
    internal const string CreatorCredit = "Created by Emmanuel Alagbe";
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 30 };
    private readonly System.Diagnostics.Stopwatch elapsed = new();
    private double? previewMilliseconds;
    internal LaunchArtwork()
    {
        DoubleBuffered = true; BackColor = Theme.Canvas; AccessibleName = "Experiment Designer. Preparing your workspace."; AccessibleDescription = CreatorCredit;
        AccessibleRole = AccessibleRole.ProgressBar;
        animation.Tick += (_, _) => Invalidate();
    }
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) { elapsed.Restart(); animation.Start(); } else animation.Stop();
    }
    internal static double OrbitAngle(double milliseconds) => milliseconds / 5000 * Math.PI * 2;
    internal void PreviewFrame(double milliseconds) { previewMilliseconds = milliseconds; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if (Width < 2 || Height < 2) return;
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = Math.Min(Width / 600f, Height / 380f);
        float ox = (Width - 600 * scale) / 2, oy = (Height - 380 * scale) / 2;
        Rectangle BoundsAt(float x, float y, float w, float h) => new((int)(ox + x * scale), (int)(oy + y * scale), (int)(w * scale), (int)(h * scale));
        using var surface = new LinearGradientBrush(ClientRectangle, Theme.Panel, Theme.Canvas, LinearGradientMode.ForwardDiagonal);
        g.FillRectangle(surface, ClientRectangle);
        double ms = previewMilliseconds ?? elapsed.Elapsed.TotalMilliseconds;
        using var halo = new Pen(Color.FromArgb(48, Theme.Teal), Math.Max(1, scale));
        g.DrawEllipse(halo, BoundsAt(236, 38, 128, 128));
        for (int i = 0; i < 3; i++)
        {
            double angle = OrbitAngle(ms) - i * .16 - Math.PI / 2;
            float x = 300 + (float)Math.Cos(angle) * 64, y = 102 + (float)Math.Sin(angle) * 64;
            using var dot = new SolidBrush(Color.FromArgb(230 - i * 65, Theme.Teal));
            g.FillEllipse(dot, BoundsAt(x - 3, y - 3, 6, 6));
        }
        g.DrawImage(Brand.Logo, BoundsAt(252, 54, 96, 96));
        using var headingBase = Theme.Heading(); using var bodyBase = Theme.Body();
        using var heading = new Font(headingBase.FontFamily, 31 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        using var subtitle = new Font(bodyBase.FontFamily, 14 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        using var caption = new Font(bodyBase.FontFamily, 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(g, "Experiment Designer", heading, BoundsAt(32, 183, 536, 46), Theme.Text, flags);
        TextRenderer.DrawText(g, "Precision in experimental allocation", subtitle, BoundsAt(32, 241, 536, 26), Theme.Muted, flags);
        using var creator = new Font(bodyBase.FontFamily, 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, CreatorCredit, creator, BoundsAt(32, 274, 536, 22), Theme.Secondary, flags);
        using var divider = new Pen(Theme.Divider, Math.Max(1, scale));
        g.DrawLine(divider, ox + 52 * scale, oy + 304 * scale, ox + 548 * scale, oy + 304 * scale);
        TextRenderer.DrawText(g, "Preparing your workspace", caption, BoundsAt(32, 315, 536, 22), Theme.Muted, flags);
        for (int i = 0; i < 3; i++)
        {
            int alpha = (int)(65 + 150 * (.5 + .5 * Math.Sin(ms / 350 - i * .8)));
            using var dot = new SolidBrush(Color.FromArgb(alpha, Theme.Teal));
            g.FillEllipse(dot, BoundsAt(285 + i * 12, 348, 5, 5));
        }
        using var border = new Pen(Theme.Border); g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
    protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
}

internal sealed class StartupContext : ApplicationContext
{
    private readonly SplashForm splash = new();
    internal StartupContext(Action<MainForm>? ready = null)
    {
        if (ready is not null) { splash.StartPosition = FormStartPosition.Manual; splash.Location = new(-25000, -25000); splash.ShowInTaskbar = false; }
        splash.Shown += (_, _) => splash.BeginInvoke(async () =>
        {
            try
            {
                var started = System.Diagnostics.Stopwatch.StartNew();
                var main = new MainForm(); MainForm = main;
                int remaining = SplashForm.MinimumDisplayMilliseconds - (int)started.ElapsedMilliseconds;
                if (remaining > 0) await Task.Delay(remaining);
                if (ready is not null) { main.StartPosition = FormStartPosition.Manual; main.Location = new(-25000, -25000); main.ShowInTaskbar = false; }
                main.Show(); splash.Close(); splash.Dispose();
                ready?.Invoke(main);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not start Experiment Designer: " + ex.Message, "Experiment Designer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                splash.Dispose(); MainForm?.Dispose(); ExitThread();
            }
        });
        splash.Show();
    }
}

