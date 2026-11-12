using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Fundoo.Notes.Infrastructure.Data
{
    public class NotesDbConnectionFactory
    {
        private readonly string _connectionString;

        public NotesDbConnectionFactory(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("NotesDb")
                ?? throw new InvalidOperationException(
                    "NotesDb connection string is missing.");

            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                CommandTimeout = 10
            };

            _connectionString = builder.ConnectionString;
        }

        public NpgsqlConnection CreateConnection()
        {
            return new NpgsqlConnection(_connectionString);
        }
    }
}