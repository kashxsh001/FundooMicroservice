using Fundoo.Notes.Application.Interfaces;
using Fundoo.Notes.Domain.Entity;
using Fundoo.Notes.Infrastructure.Data;
using Fundoo.Notes.Infrastructure.Resilience;      // NEW
using Npgsql;

namespace Fundoo.Notes.Infrastructure.Repositories
{
    public class LabelsRepository : ILabelsRepository
    {
        private readonly NotesDbConnectionFactory _connectionFactory;
        private readonly DbResiliencePolicies _policies;                      // NEW

        public LabelsRepository(NotesDbConnectionFactory connectionFactory, DbResiliencePolicies policies)   // CHANGED
        {
            _connectionFactory = connectionFactory;
            _policies = policies;
        }

        // CREATE -> NoRetry
        public async Task<Label> CreateAsync(Label label, CancellationToken cancellationToken = default)
        {
            label.Id = Guid.NewGuid();      // MOVED: wrap se pehle, taaki id sirf ek baar bane

            return await _policies.NoRetry.ExecuteAsync(async token =>
            {
                await using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync(token);

                const string sql = """
                    INSERT INTO labels (id, user_id, name)
                    VALUES (@id, @userId, @name)
                    RETURNING id, user_id, name;
                    """;

                await using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("id", label.Id);
                command.Parameters.AddWithValue("userId", label.UserId);
                command.Parameters.AddWithValue("name", label.Name);

                await using var reader = await command.ExecuteReaderAsync(token);
                await reader.ReadAsync(token);

                return new Label
                {
                    Id = reader.GetGuid(0),
                    UserId = reader.GetGuid(1),
                    Name = reader.GetString(2)
                };
            }, cancellationToken);
        }

        // READ -> Safe
        public Task<IReadOnlyList<Label>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default)
            => _policies.Safe.ExecuteAsync<IReadOnlyList<Label>>(async token =>
            {
                var labels = new List<Label>();

                await using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync(token);

                const string sql = "SELECT id, user_id, name FROM labels WHERE user_id = @userId ORDER BY name;";

                await using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("userId", userId);

                await using var reader = await command.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                {
                    labels.Add(new Label
                    {
                        Id = reader.GetGuid(0),
                        UserId = reader.GetGuid(1),
                        Name = reader.GetString(2)
                    });
                }

                return labels;
            }, cancellationToken);

        // RENAME -> Safe (wahi naam dobara set karna harmless)
        public Task<bool> UpdateAsync(Guid labelId, Guid userId, string name,
            CancellationToken cancellationToken = default)
            => _policies.Safe.ExecuteAsync(async token =>
            {
                await using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync(token);

                const string sql = "UPDATE labels SET name = @name WHERE id = @labelId AND user_id = @userId;";

                await using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("labelId", labelId);
                command.Parameters.AddWithValue("userId", userId);
                command.Parameters.AddWithValue("name", name);

                return await command.ExecuteNonQueryAsync(token) > 0;
            }, cancellationToken);

        // DELETE -> NoRetry
        public Task<bool> DeleteAsync(Guid labelId, Guid userId, CancellationToken cancellationToken = default)
            => _policies.NoRetry.ExecuteAsync(async token =>
            {
                await using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync(token);

                const string sql = "DELETE FROM labels WHERE id = @labelId AND user_id = @userId;";

                await using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("labelId", labelId);
                command.Parameters.AddWithValue("userId", userId);

                return await command.ExecuteNonQueryAsync(token) > 0;
            }, cancellationToken);

        // The note AND the label must both belong to the user.
        // ATTACH -> Safe: poora transaction (begin se commit tak) wrap ke andar hai,
        // aur INSERT ... ON CONFLICT DO NOTHING hai, to dobara chalana harmless hai.
        public Task<bool> AttachToNoteAsync(Guid noteId, Guid labelId, Guid userId,
            CancellationToken cancellationToken = default)
            => _policies.Safe.ExecuteAsync(async token =>
            {
                await using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync(token);

                await using var transaction = await connection.BeginTransactionAsync(token);

                try
                {
                    const string ownershipSql = """
                        SELECT EXISTS (
                            SELECT 1
                            FROM notes n
                            INNER JOIN labels l ON l.user_id = n.user_id
                            WHERE n.id = @noteId
                              AND n.user_id = @userId
                              AND l.id = @labelId
                              AND l.user_id = @userId
                        );
                        """;

                    await using var ownershipCommand = new NpgsqlCommand(ownershipSql, connection, transaction);
                    ownershipCommand.Parameters.AddWithValue("noteId", noteId);
                    ownershipCommand.Parameters.AddWithValue("labelId", labelId);
                    ownershipCommand.Parameters.AddWithValue("userId", userId);

                    var ownsBoth = (bool)(await ownershipCommand.ExecuteScalarAsync(token) ?? false);

                    if (!ownsBoth)
                    {
                        await transaction.RollbackAsync(token);
                        return false;
                    }

                    const string insertSql = """
                        INSERT INTO note_labels (note_id, label_id)
                        VALUES (@noteId, @labelId)
                        ON CONFLICT (note_id, label_id) DO NOTHING;
                        """;

                    await using var insertCommand = new NpgsqlCommand(insertSql, connection, transaction);
                    insertCommand.Parameters.AddWithValue("noteId", noteId);
                    insertCommand.Parameters.AddWithValue("labelId", labelId);

                    await insertCommand.ExecuteNonQueryAsync(token);
                    await transaction.CommitAsync(token);

                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync(token);
                    throw;
                }
            }, cancellationToken);

        // DETACH -> NoRetry
        public Task<bool> DetachFromNoteAsync(Guid noteId, Guid labelId, Guid userId,
            CancellationToken cancellationToken = default)
            => _policies.NoRetry.ExecuteAsync(async token =>
            {
                await using var connection = _connectionFactory.CreateConnection();
                await connection.OpenAsync(token);

                const string sql = """
                    DELETE FROM note_labels nl
                    USING notes n, labels l
                    WHERE nl.note_id = n.id
                      AND nl.label_id = l.id
                      AND nl.note_id = @noteId
                      AND nl.label_id = @labelId
                      AND n.user_id = @userId
                      AND l.user_id = @userId;
                    """;

                await using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("noteId", noteId);
                command.Parameters.AddWithValue("labelId", labelId);
                command.Parameters.AddWithValue("userId", userId);

                return await command.ExecuteNonQueryAsync(token) > 0;
            }, cancellationToken);
    }
}