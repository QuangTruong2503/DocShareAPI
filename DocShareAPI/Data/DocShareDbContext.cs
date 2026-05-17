using DocShareAPI.Models;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Metadata;
namespace DocShareAPI.Data
{
    public class DocShareDbContext : DbContext
    {
        public DocShareDbContext(DbContextOptions<DocShareDbContext> options) : base(options)
        {
        }
        // DbSet properties for each table
        public DbSet<Users> USERS { get; set; }
        public DbSet<Documents> DOCUMENTS { get; set; }
        public DbSet<Categories> CATEGORIES { get; set; }
        public DbSet<DocumentCategories> DOCUMENT_CATEGORIES { get; set; }
        public DbSet<Tags> TAGS { get; set; }
        public DbSet<DocumentTags> DOCUMENT_TAGS { get; set; }
        public DbSet<Collections> COLLECTIONS { get; set; }
        public DbSet<CollectionDocuments> COLLECTION_DOCUMENTS { get; set; }
        public DbSet<Follows> FOLLOWS { get; set; }
        public DbSet<Reports> REPORTS { get; set; }
        public DbSet<Tokens> TOKENS { get; set; }
        public DbSet<Likes> LIKES { get; set; }
        public DbSet<Notifications> NOTIFICATIONS { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Users>()
           .Property(u => u.two_factor_method)
           .HasConversion<string>(); // Convert enum to string

            modelBuilder.Entity<Users>()
                .HasIndex(u => u.Email)
                .IsUnique();
            modelBuilder.Entity<Users>()
                .HasIndex(u => u.Username)
                .IsUnique();
            modelBuilder.Entity<Documents>()
                .HasIndex(d => new { d.is_public, d.uploaded_at });
            modelBuilder.Entity<Documents>()
                .HasIndex(d => d.user_id);
            modelBuilder.Entity<Tokens>()
                .HasIndex(t => t.token)
                .IsUnique();
            modelBuilder.Entity<Tokens>()
                .HasIndex(t => new { t.user_id, t.type, t.is_active, t.expires_at });
            modelBuilder.Entity<Categories>()
                .HasIndex(c => c.parent_id);
            modelBuilder.Entity<Notifications>()
                .Property(n => n.notification_id)
                .ValueGeneratedOnAdd();
            modelBuilder.Entity<Notifications>()
                .Property(n => n.type)
                .HasMaxLength(50);
            modelBuilder.Entity<Notifications>()
                .Property(n => n.title)
                .HasMaxLength(150);
            modelBuilder.Entity<Notifications>()
                .Property(n => n.message)
                .HasMaxLength(1000);
            modelBuilder.Entity<Notifications>()
                .Property(n => n.target_url)
                .HasMaxLength(500);
            modelBuilder.Entity<Notifications>()
                .Property(n => n.metadata)
                .HasColumnType("json");
            modelBuilder.Entity<Notifications>()
                .Property(n => n.created_at)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            modelBuilder.Entity<Notifications>()
                .Property(n => n.updated_at)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            modelBuilder.Entity<Notifications>()
                .HasIndex(n => new { n.recipient_user_id, n.is_read, n.created_at })
                .HasDatabaseName("IX_NOTIFICATIONS_recipient_is_read_created");
            modelBuilder.Entity<Notifications>()
                .HasIndex(n => new { n.recipient_user_id, n.created_at })
                .HasDatabaseName("IX_NOTIFICATIONS_recipient_created");
            modelBuilder.Entity<Notifications>()
                .HasIndex(n => n.actor_user_id)
                .HasDatabaseName("IX_NOTIFICATIONS_actor_user_id");
            modelBuilder.Entity<Notifications>()
                .HasIndex(n => n.related_document_id)
                .HasDatabaseName("IX_NOTIFICATIONS_related_document_id");
            modelBuilder.Entity<Notifications>()
                .HasIndex(n => n.related_comment_id)
                .HasDatabaseName("IX_NOTIFICATIONS_related_comment_id");
            modelBuilder.Entity<Notifications>()
                .HasIndex(n => n.related_report_id)
                .HasDatabaseName("IX_NOTIFICATIONS_related_report_id");
            modelBuilder.Entity<Notifications>()
                .HasIndex(n => n.type)
                .HasDatabaseName("IX_NOTIFICATIONS_type");
            modelBuilder.Entity<Likes>()
                .HasIndex(l => new { l.user_id, l.document_id })
                .IsUnique();
            // Đảm bảo id tự động tăng
            modelBuilder.Entity<Collections>()
                .Property(c => c.collection_id)
                .ValueGeneratedOnAdd();
            modelBuilder.Entity<Likes>()
                .Property(l => l.like_id)
                .ValueGeneratedOnAdd();

            modelBuilder.Entity<Likes>()
                .HasOne(l => l.Users)
                .WithMany(u => u.Likes)
                .HasForeignKey(l => l.user_id);

            modelBuilder.Entity<Likes>()
                .HasOne(l => l.Documents)
                .WithMany(u => u.Likes)
                .HasForeignKey(l => l.document_id);

            modelBuilder.Entity<Tokens>()
                .HasOne(t => t.Users)
                .WithMany(u => u.Tokens)
                .HasForeignKey(t => t.user_id);
            modelBuilder.Entity<Tokens>()
                .Property(t => t.type)
                .HasConversion<string>();

            modelBuilder.Entity<Reports>()
                .HasOne(r => r.Users)
                .WithMany()
                .HasForeignKey(r => r.user_id);

            modelBuilder.Entity<Reports>()
                .HasOne(r => r.Documents)
                .WithMany()
                .HasForeignKey(r => r.document_id);

            modelBuilder.Entity<Notifications>()
                .HasOne(n => n.RecipientUser)
                .WithMany()
                .HasForeignKey(n => n.recipient_user_id)
                .HasConstraintName("FK_NOTIFICATIONS_recipient_user")
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Notifications>()
                .HasOne(n => n.ActorUser)
                .WithMany()
                .HasForeignKey(n => n.actor_user_id)
                .HasConstraintName("FK_NOTIFICATIONS_actor_user")
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Notifications>()
                .HasOne(n => n.RelatedDocument)
                .WithMany()
                .HasForeignKey(n => n.related_document_id)
                .HasConstraintName("FK_NOTIFICATIONS_document")
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Notifications>()
                .HasOne(n => n.RelatedReport)
                .WithMany()
                .HasForeignKey(n => n.related_report_id)
                .HasConstraintName("FK_NOTIFICATIONS_report")
                .OnDelete(DeleteBehavior.SetNull);

            // Cấu hình mối quan hệ Collections -> Users
            modelBuilder.Entity<Collections>()
                .HasOne(c => c.Users)              // Navigation property
                .WithMany(u => u.Collections)      // Quan hệ 1-n với Collections trong Users
                .HasForeignKey(c => c.user_id);    // Khóa ngoại là user_id
                
            //Document - User
            modelBuilder.Entity<Documents>()
                .HasOne(dc => dc.Users)
                .WithMany(d => d.Documents)
                .HasForeignKey(dc => dc.user_id);

            modelBuilder.Entity<DocumentCategories>()
                .HasKey(dc => new { dc.document_id, dc.category_id });
            modelBuilder.Entity<DocumentCategories>()
                .HasIndex(dc => dc.category_id);
            //Document - Category
            modelBuilder.Entity<DocumentCategories>()
                .HasOne(dc => dc.Documents)
                .WithMany(d => d.DocumentCategories)
                .HasForeignKey(dc => dc.document_id);
            //Category - Document
            modelBuilder.Entity<DocumentCategories>()
                .HasOne(dc => dc.Categories)
                .WithMany(c => c.DocumentCategories)
                .HasForeignKey(dc => dc.category_id);

            modelBuilder.Entity<DocumentTags>()
                .HasKey(dt => new { dt.document_id, dt.tag_id });
            modelBuilder.Entity<DocumentTags>()
                .HasIndex(dt => dt.tag_id);

            modelBuilder.Entity<DocumentTags>()
                .HasOne(dt => dt.Documents)
                .WithMany(d => d.DocumentTags)
                .HasForeignKey(dt => dt.document_id);

            modelBuilder.Entity<DocumentTags>()
                .HasOne(dt => dt.Tags)
                .WithMany(t => t.DocumentTags)
                .HasForeignKey(dt => dt.tag_id);

            modelBuilder.Entity<CollectionDocuments>()
                .HasKey(cd => new { cd.document_id, cd.collection_id });
            modelBuilder.Entity<CollectionDocuments>()
                .HasIndex(cd => cd.collection_id);

            modelBuilder.Entity<CollectionDocuments>()
                .HasOne(cd => cd.Collections)
                .WithMany(c => c.CollectionDocuments)
                .HasForeignKey(cd => cd.collection_id);

            modelBuilder.Entity<CollectionDocuments>()
                .HasOne(cd => cd.Documents)
                .WithMany(d => d.CollectionDocuments)
                .HasForeignKey(cd => cd.document_id);

            modelBuilder.Entity<Follows>()
                .HasKey(f => new { f.follower_id, f.following_id });

            modelBuilder.Entity<Follows>()
                .HasOne(f => f.Follower)
                .WithMany(u => u.Following)
                .HasForeignKey(f => f.follower_id)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Follows>()
                .HasOne(f => f.Following)
                .WithMany(u => u.Followers)
                .HasForeignKey(f => f.following_id)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
