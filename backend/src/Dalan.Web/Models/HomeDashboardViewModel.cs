using Dalan.Application.Analytics;

namespace Dalan.Web.Models;

public sealed class HomeDashboardViewModel
{
    public AnalyticsReport? Report { get; set; }
    public string? Error { get; set; }
}
