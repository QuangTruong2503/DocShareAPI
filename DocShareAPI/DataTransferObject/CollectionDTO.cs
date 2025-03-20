namespace DocShareAPI.DataTransferObject
{
    public class CollectionDTO
    {
        public required string Name { get; set; }
        public string? Description { get; set; }
        public bool is_public { get; set; }
    }
}
