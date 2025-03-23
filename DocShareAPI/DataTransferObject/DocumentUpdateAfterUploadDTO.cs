namespace DocShareAPI.DataTransferObject
{
    public class DocumentUpdateAfterUploadDTO
    {
        public required int document_id { get; set; }

        public required string title { get; set; }

        public  string? description { get; set; }

        public required bool is_public { get; set; }

        public int category_id { get; set; }
    }
}
