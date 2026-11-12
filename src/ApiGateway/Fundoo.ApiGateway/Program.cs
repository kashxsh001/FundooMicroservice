using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args); //WebApplication.CreateBuilder configures a Generic Host under the hood while embedding HTTP-specific features like Kestrel middleware pipelines.
/*The Generic Host is built on top of the ASP.NET Core hosting infrastructure and provides a consistent way to configure and run applications
 * regardless of the application type. It encapsulates common application concerns such as configuration, dependency injection, logging, 
 * and application lifetime management.*/
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var authLimit = builder.Configuration.GetSection("RateLimiting:Auth");
var notesLimit = builder.Configuration.GetSection("RateLimiting:Notes");

// One counter per client IP, per policy.
static RateLimitPartition<string> CreatePartition(HttpContext context, IConfigurationSection section)
{
    var clientKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    return RateLimitPartition.GetFixedWindowLimiter(clientKey, _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = section.GetValue("PermitLimit", 5),
        Window = TimeSpan.FromSeconds(section.GetValue("WindowSeconds", 60)),
        QueueLimit = 0,
        AutoReplenishment = true
    });
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth-policy", context => CreatePartition(context, authLimit));
    options.AddPolicy("notes-policy", context => CreatePartition(context, notesLimit));

    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }

        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"isSuccess\":false,\"message\":\"Too many requests. Please try again later.\"}",
            cancellationToken);
    };
});

var app = builder.Build();

app.UseRateLimiter();   // must be before MapReverseProxy
app.MapReverseProxy();

app.Run();