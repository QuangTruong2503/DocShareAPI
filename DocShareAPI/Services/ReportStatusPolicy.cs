namespace DocShareAPI.Services;

public static class ReportStatusPolicy
{
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        ["pending"] = "Chờ giải quyết", ["processing"] = "Đang xử lý", ["resolved"] = "Đã xử lý", ["rejected"] = "Từ chối"
    };
    public static string Label(string value) => Labels.GetValueOrDefault(value, value);
    public static string Code(string value) => Labels.FirstOrDefault(x => x.Value == value).Key ?? value;
}
