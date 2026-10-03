using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models;

public class ExternalIdentity
{
    [MaxLength(32)] public string provider { get; set; } = "google";
    [MaxLength(255)] public string subject { get; set; } = "";
    public Guid user_id { get; set; }
    public Users User { get; set; } = null!;
}
