using Dalan.Application.Analytics;

namespace Dalan.Web.Models;

public sealed class AnalyticsViewModel
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public AnalyticsReport? Report { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public string? Error { get; set; }
}
