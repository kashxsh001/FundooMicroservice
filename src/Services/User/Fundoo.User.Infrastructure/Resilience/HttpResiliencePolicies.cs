using Microsoft.Extensions.Logging;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Extensions.Http;
using Polly.Timeout;
using System.Net;
using System.Reflection.Metadata;

namespace Fundoo.User.Infrastructure.Resilience;

public class HttpResiliencePolicies
{
    public IAsyncPolicy<HttpResponseMessage> Retry { get; }
    public IAsyncPolicy<HttpResponseMessage> Breaker { get; }
    public IAsyncPolicy<HttpResponseMessage> Timeout { get; }

    public HttpResiliencePolicies(ILogger<HttpResiliencePolicies> logger)
    {
        // RETRY: try up to 3 more times. Waits are random (jitter): about 0.5s, then longer.
        // Retries on: 5xx, 408, network errors, and our own timeout. NOT on 400/401/404.
        Retry = HttpPolicyExtensions.HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .WaitAndRetryAsync( //This is the actual retry mechanism.//It tells Polly:1.Execute the operation.//2.If a handled failure occurs, wait.
//3.Execute it again.
//4.Continue until the retry limit is reached.
//You have also specified how long Polly should wait.
                Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromMilliseconds(500), retryCount: 3),//Its parameters mean:
//-TimeSpan.FromMilliseconds(500): The median first - retry delay target.
//- retryCount: 3: Generate three retry delays.
                (outcome, delay, attempt, _) =>
                    logger.LogWarning("Retry {Attempt} after {Delay}ms", attempt, (int)delay.TotalMilliseconds));

       

        // CIRCUIT BREAKER: 5 failures in a row → stop calling for 30 seconds
        Breaker = HttpPolicyExtensions.HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30),
                (outcome, span) => logger.LogError("Circuit OPEN for {S}s", span.TotalSeconds),
                () => logger.LogInformation("Circuit CLOSED"));

        // TIMEOUT: each single try may take at most 3 seconds
        Timeout = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(3));
    }
}