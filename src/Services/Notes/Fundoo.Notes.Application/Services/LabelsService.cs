using Fundoo.Notes.Application.Interfaces;
using Fundoo.Notes.Domain.Entity;

namespace Fundoo.Notes.Application.Services
{
    public class LabelsService : ILabelsService
    {
        private readonly ILabelsRepository _labelsRepository;
        private readonly IUserNotesCache _userNotesCache;

        public LabelsService(ILabelsRepository labelsRepository, IUserNotesCache userNotesCache)
        {
            _labelsRepository = labelsRepository;
            _userNotesCache = userNotesCache;
        }

        public async Task<Label> CreateAsync(Guid userId, string name, CancellationToken cancellationToken = default)
        {
            ValidateName(name);

            var label = new Label
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = name.Trim()
            };

            var created = await _labelsRepository.CreateAsync(label, cancellationToken);
            await _userNotesCache.InvalidateAsync(userId, cancellationToken);
            return created;
        }

        public Task<IReadOnlyList<Label>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default)
            => _labelsRepository.GetAllAsync(userId, cancellationToken);

        public async Task<bool> UpdateAsync(Guid labelId, Guid userId, string name,
            CancellationToken cancellationToken = default)
        {
            ValidateName(name);
            var updated = await _labelsRepository.UpdateAsync(labelId, userId, name.Trim(), cancellationToken);
            if (updated) await _userNotesCache.InvalidateAsync(userId, cancellationToken);
            return updated;
        }

        public async Task<bool> DeleteAsync(Guid labelId, Guid userId, CancellationToken cancellationToken = default)
        {
            var deleted = await _labelsRepository.DeleteAsync(labelId, userId, cancellationToken);
            if (deleted) await _userNotesCache.InvalidateAsync(userId, cancellationToken);
            return deleted;
        }

        public async Task<bool> AttachToNoteAsync(Guid noteId, Guid labelId, Guid userId,
            CancellationToken cancellationToken = default)
        {
            var attached = await _labelsRepository.AttachToNoteAsync(noteId, labelId, userId, cancellationToken);
            if (attached) await _userNotesCache.InvalidateAsync(userId, cancellationToken);
            return attached;
        }

        public async Task<bool> DetachFromNoteAsync(Guid noteId, Guid labelId, Guid userId,
            CancellationToken cancellationToken = default)
        {
            var detached = await _labelsRepository.DetachFromNoteAsync(noteId, labelId, userId, cancellationToken);
            if (detached) await _userNotesCache.InvalidateAsync(userId, cancellationToken);
            return detached;
        }

        private static void ValidateName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Label name is required.");

            if (name.Trim().Length > 100)
                throw new ArgumentException("Label name cannot exceed 100 characters.");
        }
    }
}
