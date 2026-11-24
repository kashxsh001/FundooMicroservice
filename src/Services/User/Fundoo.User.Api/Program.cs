using Fundoo.User.Infrastructure.Resilience;
using Fundoo.User.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
// Local development: always load user secrets, even if VS starts the app without the Development profile
builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger with an "Authorize" button for JWT
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT access token."
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

// DbContext + repository + auth services (reads ConnectionStrings:UserDb)
builder.Services.AddUserInfrastructure(builder.Configuration);

// JWT validation
var signingKey = builder.Configuration["Jwt:SigningKey"]?? throw new InvalidOperationException("JWT signing key is not configured.");
var issuer = builder.Configuration["Jwt:Issuer"]?? throw new InvalidOperationException("JWT issuer is not configured.");
var audience = builder.Configuration["Jwt:Audience"]?? throw new InvalidOperationException("JWT audience is not configured.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero//Allows small clock differences between servers. Default is 5 minutes, which silently extends expiry, so shrinking it is common
        };
    })
    .AddCookie("External", o =>
     {
         o.Cookie.Name = "fundoo.external";
         o.ExpireTimeSpan = TimeSpan.FromMinutes(5);
     })
    // NEW: Google login
    .AddGoogle("Google", o =>
    {
        o.SignInScheme = "External";
        o.ClientId = builder.Configuration["Google:ClientId"]
            ?? throw new InvalidOperationException("Google:ClientId is not configured.");
        o.ClientSecret = builder.Configuration["Google:ClientSecret"]
            ?? throw new InvalidOperationException("Google:ClientSecret is not configured.");
        o.ClaimActions.MapJsonKey("email_verified", "email_verified");   // Google ne email verify kiya ya nahi
    });

// HttpClient used by NotificationClient to call the Notification API
builder.Services.AddHttpClient("NotificationService", client =>
{
    var baseUrl = builder.Configuration["NotificationService:BaseUrl"]?? throw new InvalidOperationException("NotificationService:BaseUrl is not configured.");
    client.BaseAddress = new Uri(baseUrl);
});
builder.Services.AddSingleton<TotpService>();
// Keep the encryption key ring stable across Kubernetes container replacements.
// This path is backed by a dedicated persistent volume in the K3s deployment.
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysDirectory"]
    ?? "/var/lib/fundoo/data-protection-keys";
builder.Services.AddDataProtection()
    .SetApplicationName("FundooMicroService")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
builder.Services.AddSingleton<HttpResiliencePolicies>();

builder.Services.AddHttpClient("NotificationService", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["NotificationService:BaseUrl"]
        ?? throw new InvalidOperationException("NotificationService:BaseUrl missing"));
    client.Timeout = TimeSpan.FromSeconds(20);   // total limit for everything (all tries together)
})
.AddPolicyHandler((sp, _) => sp.GetRequiredService<HttpResiliencePolicies>().Retry)     // outer
.AddPolicyHandler((sp, _) => sp.GetRequiredService<HttpResiliencePolicies>().Breaker)   // middle
.AddPolicyHandler((sp, _) => sp.GetRequiredService<HttpResiliencePolicies>().Timeout);  // inner

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
