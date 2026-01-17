namespace DocShareAPI.DataTransferObject.Documents
{
    public class DocumentUpdateAfterUploadDTO
    {
        public required int document_id { get; set; }

        public required string title { get; set; }

        public string? description { get; set; }

        public required bool is_public { get; set; }

        public ICollection<TageDTO>? tags { get; set; }
        public ICollection<string>? categories { get; set; }
    }
    public class TageDTO
    {
        public required string Name { get; set; }
    }
}
