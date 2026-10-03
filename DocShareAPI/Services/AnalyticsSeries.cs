namespace DocShareAPI.Services;

public record CountPoint(DateTime Date, int Count);
public static class AnalyticsSeries
{
    public static IEnumerable<object> Group(IEnumerable<CountPoint> points, string groupBy) => points
        .GroupBy(point => groupBy switch
        {
            "month" => new DateTime(point.Date.Year, point.Date.Month, 1),
            "week" => point.Date.AddDays(-((int)point.Date.DayOfWeek + 6) % 7),
            _ => point.Date
        })
        .OrderBy(group => group.Key)
        .Select(group => new { date = group.Key.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), count = group.Sum(point => point.Count) });
}
