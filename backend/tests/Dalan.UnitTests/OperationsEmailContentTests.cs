using System.Globalization;
using System.Text.Json;
using Dalan.Application.Notifications;
using Xunit;

namespace Dalan.UnitTests;

public sealed class OperationsEmailContentTests
{
    private static readonly DateOnly Day = new(2026, 11, 19);

    [Fact]
    public void Dispatch_uses_previous_utc_day_and_totals_all_saved_plans()
    {
        var content = OperationsEmailContent.Create(new(Day, "dispatch", """
            [{"Id":"plan-a","ConfirmedAtUtc":"2026-11-18T23:30:00Z","Routes":2,"Deliveries":5},
             {"Id":"plan-b","ConfirmedAtUtc":"2026-11-18T10:00:00Z","Routes":1,"Deliveries":3}]
            """));
        Assert.Equal("Dalan | Dispatch summary | 18 Nov 2026 (UTC)", content.Subject);
        Assert.Contains("18 Nov 2026, 00:00 UTC to 19 Nov 2026, 00:00 UTC (end exclusive)", content.Text);
        Assert.Contains("18 Nov 2026, 08:00 PHT", content.Text);
        Assert.Contains("Confirmed plans: 2\nRoutes planned: 3\nDelivery assignments: 8", content.Text);
        Assert.Contains("Confirmed: 19 Nov 2026, 07:30 PHT", content.Text);
        Assert.Contains("not completed deliveries", content.Text);
        Assert.DoesNotContain("Records:", content.Text);
    }

    [Fact]
    public void Empty_dispatch_explains_zero_activity_instead_of_an_opaque_record_count()
    {
        var content = OperationsEmailContent.Create(new(Day, "dispatch", "[]"));
        Assert.Contains("No dispatch plans were confirmed during this reporting period.", content.Text);
        Assert.Contains("Confirmed plans: 0\nRoutes planned: 0\nDelivery assignments: 0", content.Text);
    }

    [Fact]
    public void Overdue_shows_deadline_age_cutoff_and_safe_recipient_name()
    {
        var content = OperationsEmailContent.Create(new(Day, "overdue-14", """
            [{"Id":"delivery-a","RecipientName":"Customer\r\nInjected heading","ExpectedDeliveryAtUtc":"2026-11-19T12:30:00Z"}]
            """));
        Assert.Contains("1 overdue delivery", content.Subject);
        Assert.Contains("19 Nov 2026, 22:00 PHT (UTC+08:00)", content.Subject);
        Assert.Contains("UTC cutoff: 19 Nov 2026, 14:00 UTC", content.Text);
        Assert.Contains("Expected by: 19 Nov 2026, 20:30 PHT", content.Text);
        Assert.Contains("Overdue at check: 1h 30m", content.Text);
        Assert.Contains("Recipient: Customer Injected heading", content.Text);
        Assert.DoesNotContain("\nInjected heading", content.Text);
        Assert.Contains("Next step: Review the Delivery queue", content.Text);
    }

    [Fact]
    public void Maintenance_separates_past_due_today_and_upcoming_dates()
    {
        var content = OperationsEmailContent.Create(new(Day, "maintenance", """
            [{"RegistrationNumber":"OLD-1","NextMaintenanceDate":"2026-11-16"},
             {"RegistrationNumber":"TODAY-1","NextMaintenanceDate":"2026-11-19"},
             {"RegistrationNumber":"SOON-1","NextMaintenanceDate":"2026-11-21"},
             {"RegistrationNumber":"UNKNOWN-1"}]
            """));
        Assert.Contains("4 vehicles", content.Subject);
        Assert.Contains("Past due: 1\nDue today: 1\nDue in the next 7 days: 1\nDate unavailable: 1", content.Text);
        Assert.Contains("Vehicle: OLD-1\n  Maintenance due: 16 Nov 2026 | 3 days overdue", content.Text);
        Assert.Contains("Due in 2 days", content.Text);
        Assert.Contains("through 26 Nov 2026 (inclusive)", content.Text);
    }

    [Fact]
    public void Detail_limit_does_not_truncate_totals_or_hide_that_more_items_exist()
    {
        var rows = Enumerable.Range(1, 25).Select(i => new { Id = "plan-" + i, Routes = 2, Deliveries = 3 });
        var content = OperationsEmailContent.Create(new(Day, "dispatch", JsonSerializer.Serialize(rows)));
        Assert.Contains("Routes planned: 50\nDelivery assignments: 75", content.Text);
        Assert.Contains("Showing the first 20 of 25 items", content.Text);
        Assert.Contains("Plan: plan-20\n", content.Text);
        Assert.DoesNotContain("Plan: plan-21\n", content.Text);
    }

    [Fact]
    public void Legacy_missing_fields_are_unavailable_rather_than_invented_zeroes()
    {
        var dispatch = OperationsEmailContent.Create(new(Day, "dispatch", """[{"Id":"old-plan"}]"""));
        Assert.Contains("Routes planned: Unavailable\nDelivery assignments: Unavailable", dispatch.Text);
        Assert.Contains("Confirmed: Unavailable", dispatch.Text);
        var overdue = OperationsEmailContent.Create(new(Day, "overdue-14", """[{"Id":"old-delivery","ExpectedDeliveryAtUtc":"invalid"}]"""));
        Assert.Contains("Expected by: Unavailable\n  Overdue at check: Unavailable", overdue.Text);
        Assert.DoesNotContain("Recipient:", overdue.Text);
    }

    [Fact]
    public void Content_is_stable_across_host_cultures_for_the_same_snapshot()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            var notice = new OperationsNotice(Day, "dispatch", "[]");
            CultureInfo.CurrentCulture = new("en-US");
            var english = OperationsEmailContent.Create(notice);
            CultureInfo.CurrentCulture = new("ar-SA");
            Assert.Equal(english, OperationsEmailContent.Create(notice));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Theory]
    [InlineData("overdue-99")]
    [InlineData("overdue-invalid")]
    [InlineData("unknown")]
    public void Unknown_intervals_do_not_invent_a_snapshot_time(string kind)
        => Assert.Throws<ArgumentException>(() => OperationsEmailContent.Create(new(Day, kind, "[]")));
}
