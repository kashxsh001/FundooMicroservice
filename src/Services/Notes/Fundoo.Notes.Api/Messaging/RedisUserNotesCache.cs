using Fundoo.Notes.Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;

namespace Fundoo.Notes.Api.Messaging;

public sealed class RedisUserNotesCache(IDistributedCache cache, ILogger<RedisUserNotesCache> logger) : IUserNotesCache
{
    public async Task<string> GetVersionAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try { return await cache.GetStringAsync(VersionKey(userId), cancellationToken) ?? "0"; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read Redis cache version for user {UserId}", userId);
            return "redis-unavailable";
        }
    }

    public async Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try { await cache.SetStringAsync(VersionKey(userId), Guid.NewGuid().ToString("N"), cancellationToken); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not invalidate Redis cache for user {UserId}", userId); }
    }

    private static string VersionKey(Guid userId) => $"notes:user:{userId}:cache-version";
}
