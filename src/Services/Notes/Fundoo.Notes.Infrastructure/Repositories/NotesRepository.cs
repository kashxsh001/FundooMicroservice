using Fundoo.Notes.Application.Interfaces;
using Fundoo.Notes.Domain.Entity;
using Fundoo.Notes.Infrastructure.Data;
using Fundoo.Notes.Infrastructure.Resilience;      // NEW
using Npgsql;
using NpgsqlTypes;

namespace Fundoo.Notes.Infrastructure.Repositories
{
    public class NotesRepository : INotesRepository
    {
        private const string SelectColumns = """
            SELECT id, user_id, title, content, color,
                   is_pinned, is_archived, is_trashed,
                   created_at_utc, updated_at_utc
            FROM notes
            """;

        private readonly NotesDbConnectionFactory _connectionFactory;
        private readonly DbResiliencePolicies _policies;                      // NEW

        public NotesRepository(NotesDbConnectionFactory connectionFactory, DbResiliencePolicies policies)   // CHANGED
        {
            _connectionFactory = connectionFactory;
            _policies = policies;
        }

        // READ -> Safe (retry hoga)
        public Task<Note?> GetByIdAsync(Guid noteId, Guid userId, CancellationToken cancellationToken = default)
            => _policies.Safe.ExecuteAsync<Note?>(async token =>
            {
                var query = SelectColumns + " WHERE id = @noteId AND user_id = @userId;";

                await using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync(token);

                await using var command = new NpgsqlCommand(query, connection);
                command.Parameters.AddWithValue("noteId", noteId);
                command.Parameters.AddWithValue("userId", userId);

                Note note;
                await using (var reader = await command.ExecuteReaderAsync(token))
                {
                    if (!await reader.ReadAsync(token))
                        return null;

                    note = MapNote(reader);
                }

                await LoadLabelsAsync(connection, new[] { note }, userId, token);
                return note;
            }, cancellationToken);

        public Task<IReadOnlyList<Note>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default)
            => GetNotesAsync(SelectColumns + " WHERE user_id = @userId ORDER BY created_at_utc DESC;",
                userId, cancellationToken);

        public Task<IReadOnlyList<Note>> GetActiveAsync(Guid userId, CancellationToken cancellationToken = default)
            => GetNotesAsync(SelectColumns + """
                 WHERE user_id = @userId AND is_archived = FALSE AND is_trashed = FALSE
                ORDER BY is_pinned DESC, created_at_utc DESC;
                """, userId, cancellationToken);

        public Task<IReadOnlyList<Note>> GetArchivedAsync(Guid userId, CancellationToken cancellationToken = default)
            => GetNotesAsync(SelectColumns + """
                 WHERE user_id = @userId AND is_archived = TRUE AND is_trashed = FALSE
                ORDER BY updated_at_utc DESC;
                """, userId, cancellationToken);

        public Task<IReadOnlyList<Note>> GetTrashedAsync(Guid userId, CancellationToken cancellationToken = default)
            => GetNotesAsync(SelectColumns + """
                 WHERE user_id = @userId AND is_trashed = TRUE
                ORDER BY updated_at_utc DESC;
                """, userId, cancellationToken);

        // CREATE -> NoRetry
        public async Task<Note> CreateAsync(Note note, CancellationToken cancellationToken = default)
        {
            const string query = """
                INSERT INTO notes
                    (id, user_id, title, content, color, is_pinned, is_archived, is_trashed,
                     created_at_utc, updated_at_utc)
                VALUES
                    (@id, @userId, @title, @content, @color, @isPinned, @isArchived, @isTrashed,
                     @createdAtUtc, @updatedAtUtc);
                """;

            if (note.Id == Guid.Empty)
                note.Id = Guid.NewGuid();

            note.CreatedAtUtc = DateTime.UtcNow;
            note.UpdatedAtUtc = note.CreatedAtUtc;

            await _policies.NoRetry.ExecuteAsync(async token =>
            {
                await using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync(token);

                await using var command = new NpgsqlCommand(query, connection);
                command.Parameters.AddWithValue("id", note.Id);
                command.Parameters.AddWithValue("userId", note.UserId);
                command.Parameters.AddWithValue("title", note.Title);
                command.Parameters.AddWithValue("content", note.Content);
                command.Parameters.AddWithValue("color", (object?)note.Color ?? DBNull.Value);
                command.Parameters.AddWithValue("isPinned", note.IsPinned);
                command.Parameters.AddWithValue("isArchived", note.IsArchived);
                command.Parameters.AddWithValue("isTrashed", note.IsTrashed);
                command.Parameters.AddWithValue("createdAtUtc", note.CreatedAtUtc);
                command.Parameters.AddWithValue("updatedAtUtc", note.UpdatedAtUtc);

                await command.ExecuteNonQueryAsync(token);
            }, cancellationToken);

            return note;
        }

        // UPDATE -> Safe (same values dobara set karna harmless hai)
        public async Task<bool> UpdateAsync(Note note, CancellationToken cancellationToken = default)
        {
            const string query = """
                UPDATE notes
                SET title = @title,
                    content = @content,
                    color = @color,
                    is_pinned = @isPinned,
                    is_archived = @isArchived,
                    is_trashed = @isTrashed,
                    updated_at_utc = @updatedAtUtc
                WHERE id = @id AND user_id = @userId;
                """;

            note.UpdatedAtUtc = DateTime.UtcNow;

            return await _policies.Safe.ExecuteAsync(async token =>
            {
                await using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync(token);

                await using var command = new NpgsqlCommand(query, connection);
                command.Parameters.AddWithValue("id", note.Id);
                command.Parameters.AddWithValue("userId", note.UserId);
                command.Parameters.AddWithValue("title", note.Title);
                command.Parameters.AddWithValue("content", note.Content);
                command.Parameters.AddWithValue("color", (object?)note.Color ?? DBNull.Value);
                command.Parameters.AddWithValue("isPinned", note.IsPinned);
                command.Parameters.AddWithValue("isArchived", note.IsArchived);
                command.Parameters.AddWithValue("isTrashed", note.IsTrashed);
                command.Parameters.AddWithValue("updatedAtUtc", note.UpdatedAtUtc);

                return await command.ExecuteNonQueryAsync(token) > 0;
            }, cancellationToken);
        }

        // Permanently deletes only a note that is already in trash.  -> NoRetry
        public Task<bool> DeleteAsync(Guid noteId, Guid userId, CancellationToken cancellationToken = default)
            => _policies.NoRetry.ExecuteAsync(async token =>
            {
                const string query = """
                    DELETE FROM notes
                    WHERE id = @noteId AND user_id = @userId AND is_trashed = TRUE;
                    """;

                await using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync(token);

                await using var command = new NpgsqlCommand(query, connection);
                command.Parameters.AddWithValue("noteId", noteId);
                command.Parameters.AddWithValue("userId", userId);

                return await command.ExecuteNonQueryAsync(token) > 0;
            }, cancellationToken);

        // Ye ek hi method GetAll / GetActive / GetArchived / GetTrashed ko cover karta hai -> Safe
        private Task<IReadOnlyList<Note>> GetNotesAsync(string query, Guid userId,
            CancellationToken cancellationToken)
            => _policies.Safe.ExecuteAsync<IReadOnlyList<Note>>(async token =>
            {
                var notes = new List<Note>();

                await using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync(token);

                await using (var command = new NpgsqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("userId", userId);

                    await using var reader = await command.ExecuteReaderAsync(token);
                    while (await reader.ReadAsync(token))
                        notes.Add(MapNote(reader));
                }

                await LoadLabelsAsync(connection, notes, userId, token);
                return notes;
            }, cancellationToken);

        // Loads the labels of all given notes in one query.   (UNCHANGED)
        private static async Task LoadLabelsAsync(NpgsqlConnection connection, IEnumerable<Note> notes,
            Guid userId, CancellationToken cancellationToken)
        {
            var noteList = notes.ToList();
            if (noteList.Count == 0)
                return;

            const string query = """
                SELECT nl.note_id, l.id AS label_id, l.user_id AS label_user_id, l.name AS label_name
                FROM note_labels nl
                INNER JOIN labels l ON l.id = nl.label_id
                INNER JOIN notes n ON n.id = nl.note_id
                WHERE nl.note_id = ANY(@noteIds)
                  AND n.user_id = @userId
                  AND l.user_id = @userId
                ORDER BY l.name;
                """;

            await using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("noteIds",
                NpgsqlDbType.Array | NpgsqlDbType.Uuid,
                noteList.Select(n => n.Id).ToArray());
            command.Parameters.AddWithValue("userId", userId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var notesById = noteList.ToDictionary(n => n.Id);

            while (await reader.ReadAsync(cancellationToken))
            {
                var noteId = reader.GetGuid(reader.GetOrdinal("note_id"));
                if (!notesById.TryGetValue(noteId, out var note))
                    continue;

                var labelId = reader.GetGuid(reader.GetOrdinal("label_id"));

                note.NoteLabels.Add(new NoteLabel
                {
                    NoteId = noteId,
                    LabelId = labelId,
                    Label = new Label
                    {
                        Id = labelId,
                        UserId = reader.GetGuid(reader.GetOrdinal("label_user_id")),
                        Name = reader.GetString(reader.GetOrdinal("label_name"))
                    }
                });
            }
        }

        // (UNCHANGED)
        private static Note MapNote(NpgsqlDataReader reader) => new Note
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            UserId = reader.GetGuid(reader.GetOrdinal("user_id")),
            Title = reader.GetString(reader.GetOrdinal("title")),
            Content = reader.GetString(reader.GetOrdinal("content")),
            Color = reader.IsDBNull(reader.GetOrdinal("color")) ? null : reader.GetString(reader.GetOrdinal("color")),
            IsPinned = reader.GetBoolean(reader.GetOrdinal("is_pinned")),
            IsArchived = reader.GetBoolean(reader.GetOrdinal("is_archived")),
            IsTrashed = reader.GetBoolean(reader.GetOrdinal("is_trashed")),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at_utc"))
        };
    }
}