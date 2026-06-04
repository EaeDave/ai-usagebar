using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AiUsagebarTray;

/// <summary>
/// A small borderless popup that renders the detailed usage panel (progress
/// bars), shown on left-click of the tray icon near the cursor. Mirrors the
/// bordered tooltip the Waybar/TUI build shows.
/// </summary>
public sealed class PanelForm : Form
{
    private UsageSnapshot _snapshot;

    // One Dark palette.
    private static readonly Color Bg = Color.FromArgb(40, 44, 52);       // #282c34
    private static readonly Color Border = Color.FromArgb(97, 175, 239); // #61afef
    private static readonly Color Fg = Color.FromArgb(171, 178, 191);    // #abb2bf
    private static readonly Color Dim = Color.FromArgb(92, 99, 112);     // #5c6370
    private static readonly Color Track = Color.FromArgb(62, 68, 81);    // #3e4451

    public PanelForm(UsageSnapshot snapshot)
    {
        _snapshot = snapshot;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Bg;
        DoubleBuffered = true;
        Width = 320;
        Height = 220;
        Padding = new Padding(16);
        // Close when it loses focus, like a tooltip/flyout.
        Deactivate += (_, _) => Hide();
    }

    public void Update(UsageSnapshot snapshot)
    {
        _snapshot = snapshot;
        Invalidate();
    }

    /// <summary>Show near the cursor, kept on-screen.</summary>
    public void ShowNearCursor()
    {
        var cur = Cursor.Position;
        var screen = Screen.FromPoint(cur).WorkingArea;
        var x = Math.Min(cur.X, screen.Right - Width - 8);
        var y = Math.Min(cur.Y, screen.Bottom - Height - 8);
        x = Math.Max(screen.Left + 8, x);
        y = Math.Max(screen.Top + 8, y);
        Location = new Point(x, y);
        Show();
        Activate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var border = new Pen(Border, 1.5f);
        g.DrawRectangle(border, 1, 1, Width - 3, Height - 3);

        using var titleFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        using var dimFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        using var titleBrush = new SolidBrush(Border);
        using var fgBrush = new SolidBrush(Fg);
        using var dimBrush = new SolidBrush(Dim);

        int x = 20;
        int y = 16;

        if (_snapshot.IsError)
        {
            using var errFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            using var errBrush = new SolidBrush(IconFactory.ColorFor(Severity.Critical));
            g.DrawString("⚠  " + _snapshot.Vendor, titleFont, errBrush, x, y);
            var rect = new RectangleF(x, y + 28, Width - 40, Height - 60);
            g.DrawString(_snapshot.ErrorMessage, errFont, fgBrush, rect);
            return;
        }

        // Title.
        var title = string.IsNullOrWhiteSpace(_snapshot.Plan) ? _snapshot.Vendor : _snapshot.Plan;
        g.DrawString(title, titleFont, titleBrush, x, y);
        y += 30;

        // Bars.
        y = DrawBar(g, x, y, "Session", _snapshot.SessionPct, _snapshot.SessionReset, font, dimFont, fgBrush, dimBrush);
        y = DrawBar(g, x, y, "Weekly", _snapshot.WeeklyPct, _snapshot.WeeklyReset, font, dimFont, fgBrush, dimBrush);
        if (!string.IsNullOrWhiteSpace(_snapshot.SonnetPct))
            y = DrawBar(g, x, y, "Sonnet", _snapshot.SonnetPct, "", font, dimFont, fgBrush, dimBrush);

        // Footer.
        g.DrawString($"Updated {_snapshot.FetchedAt:HH:mm}", dimFont, dimBrush, x, Height - 28);
    }

    private int DrawBar(Graphics g, int x, int y, string label, string pctStr, string reset,
        Font font, Font dimFont, Brush fgBrush, Brush dimBrush)
    {
        g.DrawString(label, font, fgBrush, x, y);

        var pct = ParsePct(pctStr);
        var severity = SeverityForPct(pct);
        var barColor = IconFactory.ColorFor(severity);

        int barY = y + 20;
        int barW = Width - 40 - 50; // leave room for the % text
        int barH = 10;

        using (var track = new SolidBrush(Track))
            g.FillRectangle(track, x, barY, barW, barH);
        using (var fill = new SolidBrush(barColor))
            g.FillRectangle(fill, x, barY, (int)(barW * pct / 100.0), barH);

        using var pctBrush = new SolidBrush(barColor);
        using var pctFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        g.DrawString($"{pctStr}%", pctFont, pctBrush, x + barW + 8, barY - 4);

        int next = barY + barH + 4;
        if (!string.IsNullOrWhiteSpace(reset))
        {
            g.DrawString($"Resets in {reset}", dimFont, dimBrush, x, next);
            next += 18;
        }
        return next + 8;
    }

    private static double ParsePct(string s) =>
        double.TryParse(s, out var v) ? Math.Clamp(v, 0, 100) : 0;

    private static Severity SeverityForPct(double pct) => pct switch
    {
        >= 90 => Severity.Critical,
        >= 75 => Severity.High,
        >= 50 => Severity.Mid,
        _ => Severity.Low,
    };
}
