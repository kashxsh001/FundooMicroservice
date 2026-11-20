using Fundoo.Notes.Infrastructure;
using Fundoo.Notes.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Polly.Bulkhead;
using Polly.CircuitBreaker;
using Polly.Timeout;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Emit structured JSON logs so fields such as RequestId and NoteId remain searchable.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false";
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnection;
    options.InstanceName = "fundoo:notes:";
});
builder.Services.Configure<Fundoo.Notes.Api.Messaging.KafkaOptions>(
    builder.Configuration.GetSection("Kafka"));
builder.Services.AddSingleton<Fundoo.Notes.Application.Interfaces.INoteEventPublisher,
    Fundoo.Notes.Api.Messaging.KafkaNoteEventPublisher>();
builder.Services.AddSingleton<Fundoo.Notes.Application.Interfaces.IUserNotesCache,
    Fundoo.Notes.Api.Messaging.RedisUserNotesCache>();

builder.Services.AddControllers();//This registers MVC controller-related services.
builder.Services.AddEndpointsApiExplorer();

// Swagger with an "Authorize" button for the JWT
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// Repositories + services
builder.Services.AddNotesInfrastructure(builder.Configuration);

// JWT validation: must use the SAME key/issuer/audience as the User API
var jwt = builder.Configuration.GetSection("Jwt");

var signingKey = jwt["SigningKey"]
    ?? throw new InvalidOperationException("JWT signing key is missing.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep claim names such as "sub" and "email" as they are.
        options.MapInboundClaims = false;//by default .NET renames incoming claims to long legacy names
                                         //(sub becomes http://schemas.xmlsoap.org/.../nameidentifier).
                                         //Setting it to false keeps the original short names, so FindFirst("sub") works.
                                         //That’s why the Notes API sets it.

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwt["Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();



var app = builder.Build(); //The application is built using the registered services and configuration.

app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("HttpRequest");
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    try
    {
        await next();
    }
    finally
    {
        logger.LogInformation("HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms RequestId={RequestId}",
            context.Request.Method, context.Request.Path, context.Response.StatusCode,
            System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, context.TraceIdentifier);
    }
});

app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;

    if (ex is BrokenCircuitException or TimeoutRejectedException or BulkheadRejectedException)
    {
        ctx.Response.StatusCode = 503;
        ctx.Response.Headers["Retry-After"] = "20";
        await ctx.Response.WriteAsJsonAsync(new
        {
            status = false,
            message = "Service busy, please try again shortly.",
            data = (object?)null
        });
        return;
    }

    ctx.Response.StatusCode = 500;
    await ctx.Response.WriteAsJsonAsync(new
    {
        status = false,
        message = "Something went wrong.",
        data = (object?)null
    });
}));
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();//Redirect HTTP requests to HTTPS
//app.UseRouting(); // 1. Matches the incoming URL to a controller action
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();//We map controller endpoints.scans your application for any classes inheriting from ControllerBase or Controller
                     //and automatically registers their endpoints into the centralized ASP.NET Core Routing Engine.
app.Run();//The application starts running and handles incoming requests until shutdown.
