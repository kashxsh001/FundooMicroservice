namespace Fundoo.Notes.Application.Interfaces;

public interface IUserNotesCache
{
    Task<string> GetVersionAsync(Guid userId, CancellationToken cancellationToken = default);
    Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default);
}
