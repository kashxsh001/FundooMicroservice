namespace Fundoo.Notes.Application.Interfaces;

public interface INoteEventPublisher
{
    Task PublishAsync(string eventType, Guid noteId, Guid userId, CancellationToken cancellationToken = default);
}
