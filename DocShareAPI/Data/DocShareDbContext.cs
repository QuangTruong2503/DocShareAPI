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
        public DbSet<ExternalIdentity> EXTERNAL_IDENTITIES { get; set; }
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
        public DbSet<Folders> FOLDERS { get; set; }
        public DbSet<FolderDocuments> FOLDER_DOCUMENTS { get; set; }
        public DbSet<FolderMembers> FOLDER_MEMBERS { get; set; }
        public DbSet<FolderInvites> FOLDER_INVITES { get; set; }
        public DbSet<SeoSettings> SEO_SETTINGS { get; set; }
        public DbSet<Favorites> FAVORITES { get; set; }
        public DbSet<ShareLinks> SHARE_LINKS { get; set; }
        public DbSet<Comments> COMMENTS { get; set; }
        public DbSet<DocumentViews> DOCUMENT_VIEWS { get; set; }
        public DbSet<NotificationSettings> NOTIFICATION_SETTINGS { get; set; }
        public DbSet<AuditLogs> AUDIT_LOGS { get; set; }
        public DbSet<DocumentDownloads> DOCUMENT_DOWNLOADS { get; set; }
        public DbSet<DocumentVersions> DOCUMENT_VERSIONS { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ExternalIdentity>().HasKey(e => new { e.provider, e.subject });
            modelBuilder.Entity<ExternalIdentity>().HasIndex(e => new { e.user_id, e.provider }).IsUnique();
            modelBuilder.Entity<ExternalIdentity>().HasOne(e => e.User).WithMany().HasForeignKey(e => e.user_id).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Users>()
                .Property(u => u.storage_limit_bytes)
                .HasDefaultValue(10737418240L);

            modelBuilder.Entity<SeoSettings>()
                .ToTable("SEO_SETTINGS");
            modelBuilder.Entity<SeoSettings>()
                .Property(s => s.id)
                .ValueGeneratedNever();
            modelBuilder.Entity<SeoSettings>()
                .Property(s => s.site_name)
                .HasColumnType("text");
            modelBuilder.Entity<SeoSettings>()
                .Property(s => s.site_url)
                .HasColumnType("text");
            modelBuilder.Entity<SeoSettings>()
                .Property(s => s.default_title)
                .HasColumnType("text");
            modelBuilder.Entity<SeoSettings>()
                .Property(s => s.default_description)
                .HasColumnType("text");
            modelBuilder.Entity<SeoSettings>()
                .Property(s => s.default_image)
                .HasColumnType("text");
            modelBuilder.Entity<SeoSettings>()
                .Property(s => s.locale)
                .HasMaxLength(20);
            modelBuilder.Entity<SeoSettings>()
                .Property(s => s.robots_txt)
                .HasColumnType("longtext");
            modelBuilder.Entity<SeoSettings>()
                .Property(s => s.sitemap_routes)
                .HasColumnType("json");
            modelBuilder.Entity<SeoSettings>()
                .Property(s => s.updated_at)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
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
            modelBuilder.Entity<Documents>()
                .HasIndex(d => d.deleted_at);
            modelBuilder.Entity<Documents>()
                .HasIndex(d => new { d.deleted_root_type, d.deleted_root_id });
            modelBuilder.Entity<Documents>()
                .HasIndex(d => new { d.user_id, d.deleted_at });
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
                .HasIndex(n => n.related_folder_id)
                .HasDatabaseName("IX_NOTIFICATIONS_related_folder_id");
            modelBuilder.Entity<Notifications>()
                .HasIndex(n => n.type)
                .HasDatabaseName("IX_NOTIFICATIONS_type");
            modelBuilder.Entity<Folders>()
                .Property(f => f.folder_id)
                .ValueGeneratedOnAdd();
            modelBuilder.Entity<Folders>()
                .Property(f => f.name)
                .HasMaxLength(150);
            modelBuilder.Entity<Folders>()
                .Property(f => f.visibility)
                .HasMaxLength(20)
                .HasDefaultValue("private");
            modelBuilder.Entity<Folders>()
                .Property(f => f.created_at)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            modelBuilder.Entity<Folders>()
                .Property(f => f.updated_at)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            modelBuilder.Entity<Folders>()
                .HasIndex(f => new { f.owner_user_id, f.parent_folder_id, f.name })
                .HasDatabaseName("UQ_FOLDERS_owner_parent_name")
                .IsUnique();
            modelBuilder.Entity<Folders>()
                .HasIndex(f => f.deleted_at);
            modelBuilder.Entity<Folders>()
                .HasIndex(f => new { f.deleted_root_type, f.deleted_root_id });
            modelBuilder.Entity<Folders>()
                .HasIndex(f => new { f.owner_user_id, f.deleted_at });
            modelBuilder.Entity<Folders>()
                .HasOne(f => f.OwnerUser)
                .WithMany()
                .HasForeignKey(f => f.owner_user_id)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Folders>()
                .HasOne(f => f.ParentFolder)
                .WithMany(f => f.ChildFolders)
                .HasForeignKey(f => f.parent_folder_id)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<FolderDocuments>()
                .HasKey(fd => new { fd.folder_id, fd.document_id });
            modelBuilder.Entity<FolderDocuments>()
                .HasIndex(fd => fd.document_id)
                .HasDatabaseName("UQ_FOLDER_DOCUMENTS_document_id")
                .IsUnique();
            modelBuilder.Entity<FolderDocuments>()
                .HasOne(fd => fd.Folder)
                .WithMany(f => f.FolderDocuments)
                .HasForeignKey(fd => fd.folder_id)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<FolderDocuments>()
                .HasOne(fd => fd.Document)
                .WithMany()
                .HasForeignKey(fd => fd.document_id)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<FolderDocuments>()
                .HasOne(fd => fd.AddedByUser)
                .WithMany()
                .HasForeignKey(fd => fd.added_by_user_id)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<FolderMembers>()
                .HasKey(fm => new { fm.folder_id, fm.user_id });
            modelBuilder.Entity<FolderMembers>()
                .Property(fm => fm.role)
                .HasMaxLength(20);
            modelBuilder.Entity<FolderMembers>()
                .HasIndex(fm => fm.user_id);
            modelBuilder.Entity<FolderMembers>()
                .HasOne(fm => fm.Folder)
                .WithMany(f => f.FolderMembers)
                .HasForeignKey(fm => fm.folder_id)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<FolderMembers>()
                .HasOne(fm => fm.User)
                .WithMany()
                .HasForeignKey(fm => fm.user_id)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<FolderMembers>()
                .HasOne(fm => fm.InvitedByUser)
                .WithMany()
                .HasForeignKey(fm => fm.invited_by_user_id)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<FolderInvites>()
                .Property(fi => fi.invite_id)
                .ValueGeneratedOnAdd();
            modelBuilder.Entity<FolderInvites>()
                .Property(fi => fi.role)
                .HasMaxLength(20);
            modelBuilder.Entity<FolderInvites>()
                .Property(fi => fi.status)
                .HasMaxLength(20)
                .HasDefaultValue("pending");
            modelBuilder.Entity<FolderInvites>()
                .Property(fi => fi.token)
                .HasMaxLength(128);
            modelBuilder.Entity<FolderInvites>()
                .Property(fi => fi.invitee_email)
                .HasMaxLength(255);
            modelBuilder.Entity<FolderInvites>()
                .HasIndex(fi => fi.token)
                .IsUnique();
            modelBuilder.Entity<FolderInvites>()
                .HasIndex(fi => new { fi.folder_id, fi.status, fi.invitee_user_id });
            modelBuilder.Entity<FolderInvites>()
                .HasIndex(fi => new { fi.folder_id, fi.status, fi.invitee_email });
            modelBuilder.Entity<FolderInvites>()
                .HasOne(fi => fi.Folder)
                .WithMany(f => f.FolderInvites)
                .HasForeignKey(fi => fi.folder_id)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<FolderInvites>()
                .HasOne(fi => fi.InviterUser)
                .WithMany()
                .HasForeignKey(fi => fi.inviter_user_id)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<FolderInvites>()
                .HasOne(fi => fi.InviteeUser)
                .WithMany()
                .HasForeignKey(fi => fi.invitee_user_id)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<Likes>()
                .HasIndex(l => new { l.user_id, l.document_id })
                .IsUnique();
            modelBuilder.Entity<Favorites>()
                .Property(f => f.favorite_id)
                .ValueGeneratedOnAdd();
            modelBuilder.Entity<Favorites>()
                .Property(f => f.item_type)
                .HasMaxLength(30);
            modelBuilder.Entity<Favorites>()
                .HasIndex(f => new { f.user_id, f.item_type, f.item_id })
                .IsUnique();
            modelBuilder.Entity<Favorites>()
                .HasOne(f => f.User)
                .WithMany()
                .HasForeignKey(f => f.user_id)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ShareLinks>()
                .Property(s => s.share_link_id)
                .ValueGeneratedOnAdd();
            modelBuilder.Entity<ShareLinks>()
                .Property(s => s.token)
                .HasMaxLength(128);
            modelBuilder.Entity<ShareLinks>()
                .Property(s => s.item_type)
                .HasMaxLength(30);
            modelBuilder.Entity<ShareLinks>()
                .Property(s => s.access)
                .HasMaxLength(50);
            modelBuilder.Entity<ShareLinks>()
                .Property(s => s.permission)
                .HasMaxLength(30);
            modelBuilder.Entity<ShareLinks>()
                .HasIndex(s => s.token)
                .IsUnique();
            modelBuilder.Entity<ShareLinks>()
                .HasIndex(s => new { s.owner_user_id, s.item_type, s.item_id, s.revoked_at });
            modelBuilder.Entity<ShareLinks>()
                .HasOne(s => s.OwnerUser)
                .WithMany()
                .HasForeignKey(s => s.owner_user_id)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Comments>()
                .Property(c => c.comment_id)
                .ValueGeneratedOnAdd();
            modelBuilder.Entity<Comments>()
                .Property(c => c.content)
                .HasColumnType("text");
            modelBuilder.Entity<Comments>()
                .HasIndex(c => new { c.document_id, c.created_at });
            modelBuilder.Entity<Comments>()
                .HasIndex(c => c.user_id);
            modelBuilder.Entity<Comments>()
                .HasOne(c => c.Document)
                .WithMany()
                .HasForeignKey(c => c.document_id)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Comments>()
                .HasOne(c => c.User)
                .WithMany()
                .HasForeignKey(c => c.user_id)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Comments>()
                .HasOne(c => c.ParentComment)
                .WithMany(c => c.Replies)
                .HasForeignKey(c => c.parent_comment_id)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<DocumentViews>()
                .Property(v => v.view_id)
                .ValueGeneratedOnAdd();
            modelBuilder.Entity<DocumentViews>()
                .Property(v => v.ip_hash)
                .HasMaxLength(128);
            modelBuilder.Entity<DocumentViews>()
                .Property(v => v.source)
                .HasMaxLength(50);
            modelBuilder.Entity<DocumentViews>()
                .HasIndex(v => new { v.document_id, v.viewed_at });
            modelBuilder.Entity<DocumentViews>()
                .HasIndex(v => new { v.user_id, v.viewed_at });
            modelBuilder.Entity<DocumentViews>()
                .HasOne(v => v.Document)
                .WithMany()
                .HasForeignKey(v => v.document_id)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<DocumentViews>()
                .HasOne(v => v.User)
                .WithMany()
                .HasForeignKey(v => v.user_id)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<NotificationSettings>()
                .Property(s => s.user_id)
                .ValueGeneratedNever();
            modelBuilder.Entity<NotificationSettings>()
                .HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.user_id)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AuditLogs>()
                .Property(a => a.audit_id)
                .ValueGeneratedOnAdd();
            modelBuilder.Entity<AuditLogs>()
                .Property(a => a.action)
                .HasMaxLength(100);
            modelBuilder.Entity<AuditLogs>()
                .Property(a => a.entity_type)
                .HasMaxLength(50);
            modelBuilder.Entity<AuditLogs>()
                .Property(a => a.entity_id)
                .HasMaxLength(100);
            modelBuilder.Entity<AuditLogs>()
                .Property(a => a.metadata)
                .HasColumnType("json");
            modelBuilder.Entity<AuditLogs>()
                .HasIndex(a => new { a.entity_type, a.entity_id, a.created_at });
            modelBuilder.Entity<AuditLogs>()
                .HasIndex(a => new { a.actor_user_id, a.created_at });
            modelBuilder.Entity<AuditLogs>()
                .HasOne(a => a.ActorUser)
                .WithMany()
                .HasForeignKey(a => a.actor_user_id)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<DocumentDownloads>()
                .Property(d => d.download_id)
                .ValueGeneratedOnAdd();
            modelBuilder.Entity<DocumentDownloads>()
                .Property(d => d.source)
                .HasMaxLength(50);
            modelBuilder.Entity<DocumentDownloads>()
                .Property(d => d.share_token)
                .HasMaxLength(128);
            modelBuilder.Entity<DocumentDownloads>()
                .Property(d => d.ip_hash)
                .HasMaxLength(128);
            modelBuilder.Entity<DocumentDownloads>()
                .HasIndex(d => new { d.document_id, d.downloaded_at });
            modelBuilder.Entity<DocumentDownloads>()
                .HasIndex(d => new { d.user_id, d.downloaded_at });
            modelBuilder.Entity<DocumentDownloads>()
                .HasOne(d => d.Document)
                .WithMany()
                .HasForeignKey(d => d.document_id)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<DocumentDownloads>()
                .HasOne(d => d.User)
                .WithMany()
                .HasForeignKey(d => d.user_id)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<DocumentVersions>()
                .Property(v => v.version_id)
                .ValueGeneratedOnAdd();
            modelBuilder.Entity<DocumentVersions>()
                .Property(v => v.change_note)
                .HasColumnType("text");
            modelBuilder.Entity<DocumentVersions>()
                .HasIndex(v => new { v.document_id, v.version_number })
                .IsUnique();
            modelBuilder.Entity<DocumentVersions>()
                .HasOne(v => v.Document)
                .WithMany()
                .HasForeignKey(v => v.document_id)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<DocumentVersions>()
                .HasOne(v => v.UploadedByUser)
                .WithMany()
                .HasForeignKey(v => v.uploaded_by)
                .OnDelete(DeleteBehavior.Restrict);
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
            modelBuilder.Entity<Notifications>()
                .HasOne(n => n.RelatedFolder)
                .WithMany()
                .HasForeignKey(n => n.related_folder_id)
                .HasConstraintName("FK_NOTIFICATIONS_folder")
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
