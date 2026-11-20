using Fundoo.Notes.Application.Interfaces;
using Fundoo.Notes.Domain.Entity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Fundoo.Notes.Application.Services
{
    public class NotesService : INotesService
    {
        private readonly INotesRepository _notesRepository;
        private readonly IDistributedCache _cache;
        private readonly INoteEventPublisher _events;
        private readonly IUserNotesCache _userNotesCache;
        private readonly ILogger<NotesService> _logger;

        public NotesService(INotesRepository notesRepository, IDistributedCache cache,
            INoteEventPublisher events, IUserNotesCache userNotesCache, ILogger<NotesService> logger)
        {
            _notesRepository = notesRepository;
            _cache = cache;
            _events = events;
            _userNotesCache = userNotesCache;
            _logger = logger;
        }

        public Task<Note?> GetByIdAsync(Guid noteId, Guid userId, CancellationToken cancellationToken = default)
            => GetNoteCachedAsync(noteId, userId, cancellationToken);

        public async Task<IReadOnlyList<Note>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default)
            => await GetCachedListAsync(userId, "all", () => _notesRepository.GetAllAsync(userId, cancellationToken), cancellationToken);

        public async Task<IReadOnlyList<Note>> GetActiveAsync(Guid userId, CancellationToken cancellationToken = default)
            => await GetCachedListAsync(userId, "active", () => _notesRepository.GetActiveAsync(userId, cancellationToken), cancellationToken);

        public async Task<IReadOnlyList<Note>> GetArchivedAsync(Guid userId, CancellationToken cancellationToken = default)
            => await GetCachedListAsync(userId, "archived", () => _notesRepository.GetArchivedAsync(userId, cancellationToken), cancellationToken);

        public async Task<IReadOnlyList<Note>> GetTrashedAsync(Guid userId, CancellationToken cancellationToken = default)
            => await GetCachedListAsync(userId, "trash", () => _notesRepository.GetTrashedAsync(userId, cancellationToken), cancellationToken);

        public async Task<Note> CreateAsync(Note note, CancellationToken cancellationToken = default)
        {
            ValidateTitle(note.Title);

            // A new note always starts as active.
            note.IsArchived = false;
            note.IsTrashed = false;

            var created = await _notesRepository.CreateAsync(note, cancellationToken);
            await ChangedAsync(created.Id, created.UserId, "note.created", cancellationToken);
            return created;
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

            var updated = await _notesRepository.UpdateAsync(existing, cancellationToken);
            if (updated) await ChangedAsync(existing.Id, existing.UserId, "note.updated", cancellationToken);
            return updated;
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
            var updated = await _notesRepository.UpdateAsync(note, cancellationToken);
            if (updated) await ChangedAsync(note.Id, userId, isArchived ? "note.archived" : "note.unarchived", cancellationToken);
            return updated;
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

            var updated = await _notesRepository.UpdateAsync(note, cancellationToken);
            if (updated) await ChangedAsync(note.Id, userId, isTrashed ? "note.trashed" : "note.restored", cancellationToken);
            return updated;
        }

        public async Task<bool> DeleteAsync(Guid noteId, Guid userId, CancellationToken cancellationToken = default)
        {
            var note = await _notesRepository.GetByIdAsync(noteId, userId, cancellationToken);

            // Only trashed notes can be deleted permanently.
            if (note is null || !note.IsTrashed)
                return false;

            var deleted = await _notesRepository.DeleteAsync(noteId, userId, cancellationToken);
            if (deleted) await ChangedAsync(noteId, userId, "note.deleted", cancellationToken);
            return deleted;
        }

        private async Task<T?> GetCachedAsync<T>(string key, Func<Task<T>> load, CancellationToken cancellationToken)
        {
            try
            {
                var json = await _cache.GetStringAsync(key, cancellationToken);
                if (json is not null) return JsonSerializer.Deserialize<T>(json);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Redis read failed for cache key {CacheKey}", key); }

            var value = await load();
            if (value is not null)
            {
                try
                {
                    await _cache.SetStringAsync(key, JsonSerializer.Serialize(value),
                        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) }, cancellationToken);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Redis write failed for cache key {CacheKey}", key); }
            }
            return value;
        }

        private async Task ChangedAsync(Guid noteId, Guid userId, string eventType, CancellationToken cancellationToken)
        {
            await _userNotesCache.InvalidateAsync(userId, cancellationToken);

            // Kafka is an integration notification; a broker outage must not turn a committed DB write into an HTTP failure.
            try { await _events.PublishAsync(eventType, noteId, userId, cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "Kafka publish failed for {EventType}, note {NoteId}", eventType, noteId); }
        }

        private async Task<Note?> GetNoteCachedAsync(Guid noteId, Guid userId, CancellationToken cancellationToken)
        {
            var version = await _userNotesCache.GetVersionAsync(userId, cancellationToken);
            return await GetCachedAsync($"notes:user:{userId}:v:{version}:note:{noteId}",
                () => _notesRepository.GetByIdAsync(noteId, userId, cancellationToken), cancellationToken);
        }

        private async Task<string> VersionedListKeyAsync(Guid userId, string view, CancellationToken cancellationToken)
            => $"notes:user:{userId}:v:{await _userNotesCache.GetVersionAsync(userId, cancellationToken)}:{view}";

        private async Task<IReadOnlyList<Note>> GetCachedListAsync(Guid userId, string view,
            Func<Task<IReadOnlyList<Note>>> load, CancellationToken cancellationToken)
        {
            var key = await VersionedListKeyAsync(userId, view, cancellationToken);
            var notes = await GetCachedAsync(key, async () => (await load()).ToList(), cancellationToken);
            return notes is null ? Array.Empty<Note>() : notes;
        }

        private static void ValidateTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Note title is required.");
        }
    }
}
