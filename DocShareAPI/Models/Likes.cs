using System.ComponentModel.DataAnnotations;

namespace DocShareAPI.Models
{
    public class Likes
    {
        [Key]
        public int like_id { get; set; }
        public Guid user_id { get; set; }
        public int document_id { get; set; }
        public DateTime like_at { get; set; }
        /// <summary>
        /// 1 = Like, -1 = Dislike
        /// </summary>
        public int reaction { get; set; }
        public Users? Users { get; set; }
        public Documents? Documents { get; set; }
    }
}
