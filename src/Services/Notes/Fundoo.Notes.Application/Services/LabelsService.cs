using Fundoo.Notes.Application.Interfaces;
using Fundoo.Notes.Domain.Entity;

namespace Fundoo.Notes.Application.Services
{
    public class LabelsService : ILabelsService
    {
        private readonly ILabelsRepository _labelsRepository;

        public LabelsService(ILabelsRepository labelsRepository) => _labelsRepository = labelsRepository;

        public Task<Label> CreateAsync(Guid userId, string name, CancellationToken cancellationToken = default)
        {
            ValidateName(name);

            var label = new Label
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = name.Trim()
            };

            return _labelsRepository.CreateAsync(label, cancellationToken);
        }

        public Task<IReadOnlyList<Label>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default)
            => _labelsRepository.GetAllAsync(userId, cancellationToken);

        public Task<bool> UpdateAsync(Guid labelId, Guid userId, string name,
            CancellationToken cancellationToken = default)
        {
            ValidateName(name);
            return _labelsRepository.UpdateAsync(labelId, userId, name.Trim(), cancellationToken);
        }

        public Task<bool> DeleteAsync(Guid labelId, Guid userId, CancellationToken cancellationToken = default)
            => _labelsRepository.DeleteAsync(labelId, userId, cancellationToken);

        public Task<bool> AttachToNoteAsync(Guid noteId, Guid labelId, Guid userId,
            CancellationToken cancellationToken = default)
            => _labelsRepository.AttachToNoteAsync(noteId, labelId, userId, cancellationToken);

        public Task<bool> DetachFromNoteAsync(Guid noteId, Guid labelId, Guid userId,
            CancellationToken cancellationToken = default)
            => _labelsRepository.DetachFromNoteAsync(noteId, labelId, userId, cancellationToken);

        private static void ValidateName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Label name is required.");

            if (name.Trim().Length > 100)
                throw new ArgumentException("Label name cannot exceed 100 characters.");
        }
    }
}