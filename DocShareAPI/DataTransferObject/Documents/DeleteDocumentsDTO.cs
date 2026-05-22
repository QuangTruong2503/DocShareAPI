namespace DocShareAPI.DataTransferObject.Documents
{
    public class DeleteDocumentsDTO
    {
        public ICollection<int>? document_ids { get; set; }
    }
}
