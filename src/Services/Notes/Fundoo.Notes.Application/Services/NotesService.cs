using Fundoo.Notes.Application.Interfaces;
using Fundoo.Notes.Domain.Entity;

namespace Fundoo.Notes.Application.Services
{
    public class NotesService : INotesService
    {
        private readonly INotesRepository _notesRepository;

        public NotesService(INotesRepository notesRepository) => _notesRepository = notesRepository;

        public Task<Note?> GetByIdAsync(Guid noteId, Guid userId, CancellationToken cancellationToken = default)
            => _notesRepository.GetByIdAsync(noteId, userId, cancellationToken);

        public Task<IReadOnlyList<Note>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default)
            => _notesRepository.GetAllAsync(userId, cancellationToken);

        public Task<IReadOnlyList<Note>> GetActiveAsync(Guid userId, CancellationToken cancellationToken = default)
            => _notesRepository.GetActiveAsync(userId, cancellationToken);

        public Task<IReadOnlyList<Note>> GetArchivedAsync(Guid userId, CancellationToken cancellationToken = default)
            => _notesRepository.GetArchivedAsync(userId, cancellationToken);

        public Task<IReadOnlyList<Note>> GetTrashedAsync(Guid userId, CancellationToken cancellationToken = default)
            => _notesRepository.GetTrashedAsync(userId, cancellationToken);

        public async Task<Note> CreateAsync(Note note, CancellationToken cancellationToken = default)
        {
            ValidateTitle(note.Title);

            // A new note always starts as active.
            note.IsArchived = false;
            note.IsTrashed = false;

            return await _notesRepository.CreateAsync(note, cancellationToken);
        }

        public async Task<bool> UpdateAsync(Note note, CancellationToken cancellationToken = default)
        {
            ValidateTitle(note.Title);

            // Makes sure the note belongs to the caller.
            var existing = await _notesRepository.GetByIdAsync(note.Id, note.UserId, cancellationToken);
            if (existing is null)
                return false;

            // Trashed notes cannot be edited.
            if (existing.IsTrashed)
                return false;

            existing.Title = note.Title.Trim();
            existing.Content = note.Content;
            existing.Color = note.Color;
            existing.IsPinned = note.IsPinned;

            return await _notesRepository.UpdateAsync(existing, cancellationToken);
        }

        public async Task<bool> SetArchivedAsync(Guid noteId, Guid userId, bool isArchived,
            CancellationToken cancellationToken = default)
        {
            var note = await _notesRepository.GetByIdAsync(noteId, userId, cancellationToken);
            if (note is null)
                return false;

            // Trashed notes must be restored first.
            if (note.IsTrashed)
                return false;

            note.IsArchived = isArchived;
            return await _notesRepository.UpdateAsync(note, cancellationToken);
        }

        public async Task<bool> SetTrashedAsync(Guid noteId, Guid userId, bool isTrashed,
            CancellationToken cancellationToken = default)
        {
            var note = await _notesRepository.GetByIdAsync(noteId, userId, cancellationToken);
            if (note is null)
                return false;

            if (isTrashed)
            {
                note.IsTrashed = true;
            }
            else
            {
                // Restore to the active list.
                note.IsTrashed = false;
                note.IsArchived = false;
            }

            return await _notesRepository.UpdateAsync(note, cancellationToken);
        }

        public async Task<bool> DeleteAsync(Guid noteId, Guid userId, CancellationToken cancellationToken = default)
        {
            var note = await _notesRepository.GetByIdAsync(noteId, userId, cancellationToken);

            // Only trashed notes can be deleted permanently.
            if (note is null || !note.IsTrashed)
                return false;

            return await _notesRepository.DeleteAsync(noteId, userId, cancellationToken);
        }

        private static void ValidateTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Note title is required.");
        }
    }
}