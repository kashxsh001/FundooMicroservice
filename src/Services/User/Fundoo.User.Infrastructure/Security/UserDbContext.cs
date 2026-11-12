using Fundoo.User.Domain;
using Microsoft.EntityFrameworkCore;

namespace Fundoo.User.Infrastructure.Security
{
    public class UserDbContext(DbContextOptions<UserDbContext> options) : DbContext(options)
    {
        public DbSet<AppUser> Users => Set<AppUser>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<AppUser>(e =>
            {
                e.HasKey(u => u.Id);
                e.HasIndex(u => u.NormalizedEmail).IsUnique();   // no duplicate emails
                e.Property(u => u.Email).HasMaxLength(256);
                e.Property(u => u.NormalizedEmail).HasMaxLength(256);
            });
        }
    }
}