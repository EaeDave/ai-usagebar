using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AiUsagebarTray;

/// <summary>
/// Builds tray icons at runtime: a filled rounded dot colored by severity.
/// Colors mirror the backend's One Dark severity palette so the tray matches
/// the Waybar/TUI look.
/// </summary>
public static class IconFactory
{
    // One Dark-ish severity colors (low=green, mid=yellow, high=orange, crit=red).
    public static Color ColorFor(Severity s) => s switch
    {
        Severity.Critical => Color.FromArgb(224, 108, 117), // #e06c75
        Severity.High => Color.FromArgb(209, 154, 102),     // #d19a66
        Severity.Mid => Color.FromArgb(229, 192, 123),      // #e5c07b
        _ => Color.FromArgb(152, 195, 121),                 // #98c379
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// Create a tray icon (filled dot). The caller is responsible for disposing
    /// the returned Icon AND must not leak GDI handles — see TrayApp which
    /// destroys the previous icon before swapping.
    /// </summary>
    public static Icon CreateDot(Severity severity, int size = 32)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var color = ColorFor(severity);
            var pad = size / 8f;
            var rect = new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad);

            using var fill = new SolidBrush(color);
            g.FillEllipse(fill, rect);

            // Subtle darker ring for contrast on light/dark trays alike.
            using var pen = new Pen(Color.FromArgb(120, 0, 0, 0), Math.Max(1f, size / 16f));
            g.DrawEllipse(pen, rect);
        }

        // Convert to an HICON-backed Icon, then clone to a managed copy so we can
        // free the native handle immediately (avoids GDI handle leaks on swap).
        var hicon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hicon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hicon);
        }
    }
}
