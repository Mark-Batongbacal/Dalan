using System.Globalization;
using System.Text.Json;

namespace Dalan.Application.Notifications;

public sealed record OperationsEmailContent(string Subject, string Text)
{
    public const int MaxDetailItems = 20;
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly TimeSpan PhilippineOffset = TimeSpan.FromHours(8);

    // Format the saved snapshot only: retries and repeated jobs must not depend on the current clock/data.
    public static OperationsEmailContent Create(OperationsNotice notice)
    {
        using var document = JsonDocument.Parse(notice.Data);
        var items = document.RootElement.EnumerateArray().ToArray();
        var lines = new List<string> { "Dalan | Fleet operations", "" };
        string subject;
        if (notice.Kind == "dispatch")
        {
            var start = new DateTimeOffset(notice.DayUtc.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var end = start.AddDays(1);
            subject = $"Dalan | Dispatch summary | {start.ToString("dd MMM yyyy", Culture)} (UTC)";
            lines.AddRange([
                "DAILY DISPATCH SUMMARY", "",
                $"Reporting period: {Utc(start)} to {Utc(end)} (end exclusive)",
                $"Philippine time: {Local(start)} to {Local(end)}", "",
                $"Confirmed plans: {Number(items.Length)}",
                $"Routes planned: {Total(items, "Routes")}",
                $"Delivery assignments: {Total(items, "Deliveries")}", ""
            ]);
            if (items.Length == 0) lines.Add("No dispatch plans were confirmed during this reporting period.");
            else foreach (var item in items.Take(MaxDetailItems))
            {
                lines.Add($"Plan: {Field(item, "Id")}");
                lines.Add($"  Confirmed: {Timestamp(item, "ConfirmedAtUtc")}");
                lines.Add($"  Routes: {Count(item, "Routes")} | Delivery assignments: {Count(item, "Deliveries")}");
                lines.Add("");
            }
            lines.Add("These are confirmed-plan counts, not completed deliveries or actual vehicle utilization.");
            lines.Add("Next step: Review saved dispatch plans in Dalan for route and assignment details.");
        }
        else if (notice.Kind.StartsWith("overdue-", StringComparison.Ordinal)
            && notice.Kind.Length == 10 && int.TryParse(notice.Kind.AsSpan(8), NumberStyles.None, Culture, out var hour) && hour is >= 0 and <= 23)
        {
            var cutoff = new DateTimeOffset(notice.DayUtc.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);
            subject = $"Dalan | {Number(items.Length)} overdue {(items.Length == 1 ? "delivery" : "deliveries")} | {Local(cutoff)}";
            lines.AddRange(["OVERDUE DELIVERY ALERT", "", $"Checked at: {Local(cutoff)}", $"UTC cutoff: {Utc(cutoff)}",
                "Only deadlines before this cutoff are included.", "", $"Overdue deliveries: {Number(items.Length)}", ""]);
            foreach (var item in items.Take(MaxDetailItems))
            {
                lines.Add($"Delivery: {Field(item, "Id")}");
                var recipient = Field(item, "RecipientName", "");
                if (recipient.Length > 0) lines.Add($"  Recipient: {recipient}");
                lines.Add($"  Expected by: {Timestamp(item, "ExpectedDeliveryAtUtc")}");
                var due = Time(item, "ExpectedDeliveryAtUtc");
                lines.Add($"  Overdue at check: {(due is not null && due < cutoff ? Duration(cutoff - due.Value) : "Unavailable")}");
                lines.Add("");
            }
            lines.Add("Next step: Review the Delivery queue in Dalan, check driver progress and follow up on delayed deliveries.");
        }
        else if (notice.Kind == "maintenance")
        {
            var dates = items.Select(item => Date(item, "NextMaintenanceDate")).ToArray();
            subject = $"Dalan | Maintenance reminders | {Number(items.Length)} {(items.Length == 1 ? "vehicle" : "vehicles")} | {notice.DayUtc.ToString("dd MMM yyyy", Culture)}";
            lines.AddRange(["VEHICLE MAINTENANCE REMINDERS", "", $"Maintenance check date: {notice.DayUtc.ToString("dd MMM yyyy", Culture)}",
                $"Reminder window: through {notice.DayUtc.AddDays(7).ToString("dd MMM yyyy", Culture)} (inclusive)", "",
                $"Vehicles requiring attention: {Number(items.Length)}",
                $"Past due: {Number(dates.Count(date => date < notice.DayUtc))}",
                $"Due today: {Number(dates.Count(date => date == notice.DayUtc))}",
                $"Due in the next 7 days: {Number(dates.Count(date => date > notice.DayUtc && date <= notice.DayUtc.AddDays(7)))}"]);
            if (dates.Any(date => date is null)) lines.Add($"Date unavailable: {Number(dates.Count(date => date is null))}");
            lines.Add("");
            foreach (var item in items.Take(MaxDetailItems))
            {
                var date = Date(item, "NextMaintenanceDate");
                var remaining = date?.DayNumber - notice.DayUtc.DayNumber;
                var status = remaining switch
                {
                    < 0 => $"{Number(-remaining.Value)} {(remaining == -1 ? "day" : "days")} overdue",
                    0 => "Due today",
                    > 0 => $"Due in {Number(remaining.Value)} {(remaining == 1 ? "day" : "days")}",
                    _ => "Date unavailable"
                };
                lines.Add($"Vehicle: {Field(item, "RegistrationNumber", Field(item, "Id"))}");
                lines.Add($"  Maintenance due: {(date?.ToString("dd MMM yyyy", Culture) ?? "Unavailable")} | {status}");
                lines.Add("");
            }
            lines.Add("Next step: Review the Fleet registry in Dalan, schedule maintenance and update vehicle availability as needed.");
        }
        else throw new ArgumentException("Unknown operations notification.", nameof(notice));

        if (items.Length > MaxDetailItems) lines.Add($"Showing the first {Number(MaxDetailItems)} of {Number(items.Length)} items. Review all records in Dalan.");
        lines.AddRange(["", "This is an automated Dalan snapshot. Check Dalan for the latest operational status."]);
        return new(subject, string.Join("\n", lines));
    }

    private static string Number(long value) => value.ToString("N0", Culture);
    private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("dd MMM yyyy, HH:mm 'UTC'", Culture);
    private static string Local(DateTimeOffset value) => value.ToOffset(PhilippineOffset).ToString("dd MMM yyyy, HH:mm 'PHT (UTC+08:00)'", Culture);
    private static string Timestamp(JsonElement item, string name) => Time(item, name) is DateTimeOffset value ? Local(value) : "Unavailable";
    private static DateTimeOffset? Time(JsonElement item, string name) => item.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var date) ? date : null;
    private static DateOnly? Date(JsonElement item, string name) => item.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", Culture, DateTimeStyles.None, out var date) ? date : null;
    private static string Field(JsonElement item, string name, string fallback = "Unavailable")
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)) return fallback;
        var text = string.Join(" ", value.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length == 0 ? fallback : text[..Math.Min(text.Length, 120)];
    }
    private static long? Integer(JsonElement item, string name) => item.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0 ? number : null;
    private static string Count(JsonElement item, string name) => Integer(item, name) is long value ? Number(value) : "Unavailable";
    private static string Total(JsonElement[] items, string name)
    {
        var values = items.Select(item => Integer(item, name)).ToArray();
        if (values.Any(value => value is null)) return "Unavailable";
        try { return Number(values.Sum(value => value!.Value)); }
        catch (OverflowException) { return "Unavailable"; }
    }
    private static string Duration(TimeSpan value)
    {
        var minutes = (long)Math.Floor(value.TotalMinutes);
        if (minutes == 0) return "Less than 1 minute";
        var parts = new List<string>();
        if (minutes / 1440 > 0) parts.Add($"{Number(minutes / 1440)}d");
        if (minutes % 1440 / 60 > 0) parts.Add($"{Number(minutes % 1440 / 60)}h");
        if (minutes % 60 > 0) parts.Add($"{Number(minutes % 60)}m");
        return string.Join(" ", parts);
    }
}
