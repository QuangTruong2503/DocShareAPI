using Microsoft.EntityFrameworkCore;
namespace DocShareAPI.Data
{
    public class DocShareDbContext : DbContext
    {
        public DocShareDbContext(DbContextOptions<DocShareDbContext> options) : base(options)
        {
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }
    }
}
