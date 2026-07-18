using System.Text;
using CareTrack.Domain;

namespace CareTrack.Application;

/// <summary>
/// Renders an appointment as an RFC 5545 iCalendar document — the download
/// fallback for users who haven't connected Google or Apple calendar. The
/// event UID is the appointment id, so re-downloading after an edit updates
/// the same event in the user's calendar (via SEQUENCE/DTSTAMP).
/// </summary>
public static class IcsCalendarWriter
{
    public const string ContentType = "text/calendar; charset=utf-8";

    public static string Write(
        Appointment appt, DateTimeOffset generatedAt, string? providerName = null)
    {
        var sb = new StringBuilder();
        AppendLine(sb, "BEGIN:VCALENDAR");
        AppendLine(sb, "VERSION:2.0");
        AppendLine(sb, "PRODID:-//CareTrack//CareTrack API//EN");
        AppendLine(sb, "METHOD:PUBLISH");
        AppendLine(sb, "BEGIN:VEVENT");
        AppendLine(sb, $"UID:caretrack-{appt.Id}");
        AppendLine(sb, $"DTSTAMP:{FormatUtc(generatedAt)}");
        AppendLine(sb, $"DTSTART:{FormatUtc(appt.StartsAt)}");
        AppendLine(sb, $"DTEND:{FormatUtc(appt.EndsAt)}");
        AppendLine(sb, $"SUMMARY:{Escape(appt.Title)}");
        if (!string.IsNullOrWhiteSpace(appt.Location))
            AppendLine(sb, $"LOCATION:{Escape(appt.Location)}");
        if (!string.IsNullOrWhiteSpace(providerName))
            AppendLine(sb, $"DESCRIPTION:{Escape($"With {providerName}")}");
        AppendLine(sb, "END:VEVENT");
        AppendLine(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    /// <summary>iCalendar UTC date-time form (e.g. 20260118T143000Z).</summary>
    private static string FormatUtc(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'");

    /// <summary>RFC 5545 §3.3.11 TEXT escaping.</summary>
    private static string Escape(string value)
        => value
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n")
            .Replace("\r", "\\n");

    /// <summary>
    /// Appends a content line folded at 75 octets per RFC 5545 §3.1, with the
    /// mandatory CRLF terminator.
    /// </summary>
    private static void AppendLine(StringBuilder sb, string line)
    {
        const int limit = 75;
        var bytes = Encoding.UTF8.GetBytes(line);
        if (bytes.Length <= limit)
        {
            sb.Append(line).Append("\r\n");
            return;
        }

        // Fold on character boundaries, counting octets so multi-byte UTF-8
        // characters never straddle a fold.
        var octets = 0;
        var first = true;
        var segment = new StringBuilder();
        foreach (var ch in line)
        {
            var chOctets = Encoding.UTF8.GetByteCount(new[] { ch });
            var budget = first ? limit : limit - 1; // continuation lines lose one to the leading space
            if (octets + chOctets > budget)
            {
                sb.Append(first ? "" : " ").Append(segment).Append("\r\n");
                segment.Clear();
                octets = 0;
                first = false;
            }
            segment.Append(ch);
            octets += chOctets;
        }
        if (segment.Length > 0)
            sb.Append(first ? "" : " ").Append(segment).Append("\r\n");
    }
}
