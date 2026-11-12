using Microsoft.Extensions.Logging;
using Npgsql;
using Polly;
using Polly.Bulkhead;
using Polly.CircuitBreaker;
using Polly.Contrib.WaitAndRetry;
using Polly.Timeout;

namespace Fundoo.Notes.Infrastructure.Resilience;

public class DbResiliencePolicies
{
    public IAsyncPolicy Safe { get; }      // for reads and updates: Retry + Breaker + Bulkhead + Timeout
    public IAsyncPolicy NoRetry { get; }   // for create and delete: Breaker + Bulkhead + Timeout (no retry!)

    public DbResiliencePolicies(ILogger<DbResiliencePolicies> logger)
    {
        var retry = Policy.Handle<Exception>(IsTransient).WaitAndRetryAsync(
            Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromMilliseconds(200), 3),
            (ex, delay, attempt, _) => logger.LogWarning("DB retry {A} after {D}ms", attempt, (int)delay.TotalMilliseconds));

        // one breaker and one bulkhead shared by BOTH policies
        var breaker = Policy.Handle<Exception>(IsTransient).CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
        var bulkhead = Policy.BulkheadAsync(maxParallelization: 20, maxQueuingActions: 10);
        var timeout = Policy.TimeoutAsync(TimeSpan.FromSeconds(20));

        Safe = Policy.WrapAsync(retry, breaker, bulkhead, timeout);
        NoRetry = Policy.WrapAsync(breaker, bulkhead, timeout);
    }

    // A "temporary" DB problem = worth retrying
    private static bool IsTransient(Exception ex) => ex switch
    {
        BrokenCircuitException or BulkheadRejectedException => false,   // never retry these
        NpgsqlException n when n.IsTransient => true,
        TimeoutException or TimeoutRejectedException => true,
        _ => ex.InnerException is not null && IsTransient(ex.InnerException)
    };
}