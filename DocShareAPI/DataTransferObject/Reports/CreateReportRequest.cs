namespace DocShareAPI.DataTransferObject.Reports
{
    public class CreateReportRequest
    {
        public int DocumentId { get; set; }

        public string? Reason { get; set; }
    }
}
