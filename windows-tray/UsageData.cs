using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiUsagebarTray;

/// <summary>
/// Severity class emitted by ai-usagebar's JSON ("class" field), used to color
/// the tray icon. Mirrors the backend's waybar::Class enum.
/// </summary>
public enum Severity
{
    Low,
    Mid,
    High,
    Critical,
}

/// <summary>
/// A parsed snapshot of one vendor's usage, ready for the tray to render.
/// Built from `ai-usagebar --vendor X --format '...' --json`.
/// </summary>
public sealed record UsageSnapshot
{
    public required string Vendor { get; init; }
    public Severity Severity { get; init; } = Severity.Low;

    // Structured fields (best-effort; empty when a vendor doesn't expose them).
    public string Plan { get; init; } = "";
    public string SessionPct { get; init; } = "";
    public string SessionReset { get; init; } = "";
    public string WeeklyPct { get; init; } = "";
    public string WeeklyReset { get; init; } = "";
    public string SonnetPct { get; init; } = "";

    // True when the backend reported an error (we keep the message for tooltip).
    public bool IsError { get; init; }
    public string ErrorMessage { get; init; } = "";

    public DateTime FetchedAt { get; init; } = DateTime.Now;

    /// <summary>
    /// Build a human-readable tooltip (plain text — the Windows NotifyIcon
    /// tooltip does not render Pango markup and is capped at ~127 chars).
    /// </summary>
    public string ToTooltip()
    {
        if (IsError)
            return Truncate($"{Vendor}: {ErrorMessage}", 127);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Plan)) parts.Add(Plan);
        if (!string.IsNullOrWhiteSpace(SessionPct))
            parts.Add($"Session {SessionPct}%" +
                      (string.IsNullOrWhiteSpace(SessionReset) ? "" : $" ({SessionReset})"));
        if (!string.IsNullOrWhiteSpace(WeeklyPct))
            parts.Add($"Weekly {WeeklyPct}%" +
                      (string.IsNullOrWhiteSpace(WeeklyReset) ? "" : $" ({WeeklyReset})"));
        var s = string.Join("  ·  ", parts);
        // Fall back to the vendor name (never the raw delimited payload) if no
        // structured field was present.
        return Truncate(string.IsNullOrWhiteSpace(s) ? Vendor : s, 127);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}

/// <summary>
/// Parses the JSON ai-usagebar emits and our pipe-delimited --format payload.
/// </summary>
public static class UsageParser
{
    // Strips Pango <span ...>...</span> markup the backend wraps text in.
    private static readonly Regex PangoTag = new("<[^>]*>", RegexOptions.Compiled);

    // The --format string we pass to the backend. Order matters: it must match
    // the split below. Using '\u001f' (ASCII Unit Separator) avoids clashing
    // with any human text in the fields.
    public const string FormatString =
        "{plan}\u001f{session_pct}\u001f{session_reset}\u001f{weekly_pct}\u001f{weekly_reset}\u001f{sonnet_pct}";

    public static UsageSnapshot Parse(string vendor, string jsonLine)
    {
        // ai-usagebar always emits one JSON object: {text, tooltip, class}.
        using var doc = JsonDocument.Parse(jsonLine);
        var root = doc.RootElement;

        var rawText = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var rawTooltip = root.TryGetProperty("tooltip", out var tt) ? tt.GetString() ?? "" : "";
        var cls = root.TryGetProperty("class", out var c) ? c.GetString() ?? "low" : "low";

        var severity = cls.ToLowerInvariant() switch
        {
            "critical" => Severity.Critical,
            "high" => Severity.High,
            "mid" => Severity.Mid,
            _ => Severity.Low,
        };

        var cleanText = StripPango(rawText);

        // Detect the backend's error fallback: text is "⚠" and tooltip carries
        // the message. (See widget::run::fallback / WaybarOutput::error.)
        if (cleanText.Trim() == "⚠"
            || (severity == Severity.Critical && cleanText.Contains('⚠')))
        {
            return new UsageSnapshot
            {
                Vendor = vendor,
                Severity = Severity.Critical,
                IsError = true,
                ErrorMessage = StripPango(rawTooltip).Replace("\n", " ").Trim(),
            };
        }

        // The cleaned text is our pipe payload: plan\u001fsession_pct\u001f...
        var fields = cleanText.Split('\u001f');
        string F(int i) => i < fields.Length ? fields[i].Trim() : "";

        return new UsageSnapshot
        {
            Vendor = vendor,
            Severity = severity,
            Plan = F(0),
            SessionPct = F(1),
            SessionReset = F(2),
            WeeklyPct = F(3),
            WeeklyReset = F(4),
            SonnetPct = F(5),
        };
    }

    public static string StripPango(string s) =>
        System.Net.WebUtility.HtmlDecode(PangoTag.Replace(s, "")).Trim();
}
