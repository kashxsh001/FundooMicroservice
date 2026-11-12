using Fundoo.User.Domain;

namespace Fundoo.User.Application.Interfaces
{
    public interface IUserRepository
    {
        Task<AppUser?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
        Task<AppUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task AddAsync(AppUser user, CancellationToken cancellationToken = default);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);
        Task<AppUser?> GetByGoogleIdAsync(string googleId, CancellationToken cancellationToken = default);
    }
}