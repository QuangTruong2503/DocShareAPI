using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.DataTransferObject.Folders
{
    public class CreateFolderDto
    {
        [Required]
        [MaxLength(150)]
        public required string name { get; set; }
        public string? description { get; set; }
        public int? parent_folder_id { get; set; }
        public string? visibility { get; set; }
    }

    public class UpdateFolderDto
    {
        [MaxLength(150)]
        public string? name { get; set; }
        public string? description { get; set; }
        public string? visibility { get; set; }
    }

    public class FolderDocumentDto
    {
        public int document_id { get; set; }
    }

    public class MoveDocumentFolderDto
    {
        public int target_folder_id { get; set; }
    }

    public class AddFolderMemberDto
    {
        public Guid user_id { get; set; }
        public required string role { get; set; }
    }

    public class UpdateFolderMemberRoleDto
    {
        public required string role { get; set; }
    }

    public class CreateFolderInviteDto
    {
        public Guid? invitee_user_id { get; set; }
        [EmailAddress]
        public string? invitee_email { get; set; }
        public required string role { get; set; }
    }
}
