using BehavioralAuthSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace BehavioralAuthSystem.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<BehavioralPattern> BehavioralPatterns => Set<BehavioralPattern>();
        public DbSet<AuthLog> AuthLogs => Set<AuthLog>();
        public DbSet<FileRecord> Files => Set<FileRecord>();
        public DbSet<FileVersion> FileVersions => Set<FileVersion>();
        public DbSet<ShareLink> ShareLinks => Set<ShareLink>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(u => u.Username).IsUnique();

                entity.HasIndex(u => u.Email)
                      .IsUnique()
                      .HasFilter("[Email] IS NOT NULL");

                entity.Property(u => u.Email)
                      .HasMaxLength(320);
            });

            modelBuilder.Entity<BehavioralPattern>(entity =>
            {
                entity.HasIndex(bp => new { bp.UserId, bp.ActionType });

                entity.HasOne(bp => bp.User)
                      .WithMany(u => u.BehavioralPatterns)
                      .HasForeignKey(bp => bp.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AuthLog>(entity =>
            {
                entity.HasIndex(al => new { al.UserId, al.AttemptTime });

                entity.HasOne(al => al.User)
                      .WithMany(u => u.AuthLogs)
                      .HasForeignKey(al => al.UserId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<FileRecord>(entity =>
            {
                entity.HasIndex(f => new { f.UserId, f.IsDeleted });

                entity.HasOne(f => f.User)
                      .WithMany(u => u.Files)
                      .HasForeignKey(f => f.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<FileVersion>(entity =>
            {
                entity.HasIndex(fv => new { fv.FileId, fv.VersionNumber });

                entity.HasOne(fv => fv.FileRecord)
                      .WithMany(f => f.Versions)
                      .HasForeignKey(fv => fv.FileId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ShareLink>(entity =>
            {
                entity.HasIndex(sl => sl.Token).IsUnique();

                entity.HasOne(sl => sl.FileRecord)
                      .WithMany(f => f.ShareLinks)
                      .HasForeignKey(sl => sl.FileId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
