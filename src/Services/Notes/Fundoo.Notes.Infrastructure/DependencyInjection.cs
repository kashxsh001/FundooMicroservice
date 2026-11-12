using Fundoo.Notes.Application.Interfaces;
using Fundoo.Notes.Application.Services;
using Fundoo.Notes.Infrastructure.Data;
using Fundoo.Notes.Infrastructure.Repositories;
using Fundoo.Notes.Infrastructure.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fundoo.Notes.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddNotesInfrastructure(this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddSingleton<NotesDbConnectionFactory>();

            services.AddScoped<INotesRepository, NotesRepository>();
            services.AddScoped<INotesService, NotesService>();

            services.AddScoped<ILabelsRepository, LabelsRepository>();
            services.AddScoped<ILabelsService, LabelsService>();
            var connectionString = configuration.GetConnectionString("NotesDb")
                ?? throw new InvalidOperationException("UserDb connection string is not configured.");
          
            services.AddSingleton<DbResiliencePolicies>();
            services.AddScoped<NotesRepository>();                       // the real one
            

            return services;
        }
    }
}