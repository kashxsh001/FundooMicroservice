using Fundoo.User.Application.Interfaces;
using Fundoo.User.Domain;
using Microsoft.EntityFrameworkCore;

namespace Fundoo.User.Infrastructure.Security
{
    public class UserRepository : IUserRepository
    {
        private readonly UserDbContext _context;

        public UserRepository(UserDbContext context) => _context = context;

        public async Task<AppUser?> GetByNormalizedEmailAsync(string normalizedEmail,
            CancellationToken cancellationToken = default)
            => await _context.Users.FirstOrDefaultAsync(
                u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        public async Task<AppUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
          => await _context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        public async Task AddAsync(AppUser user, CancellationToken cancellationToken = default)
            => await _context.Users.AddAsync(user, cancellationToken);

        public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
            => await _context.SaveChangesAsync(cancellationToken);
        public Task<AppUser?> GetByGoogleIdAsync(string googleId, CancellationToken cancellationToken = default)
    => _context.Users.FirstOrDefaultAsync(u => u.GoogleId == googleId, cancellationToken);
    }
}