using System.Threading.RateLimiting;
using CallControl.Api.Application.QueueManagement;
using CallControl.Api.Hubs;
using CallControl.Api.Infrastructure.QueueManagement.Ingestion;
using CallControl.Api.Infrastructure.QueueManagement.Persistence;
using CallControl.Api.Infrastructure.QueueManagement.Persistence.Repositories;
using CallControl.Api.Infrastructure.QueueManagement.Xapi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi.Models;

namespace CallControl.Api.Infrastructure.QueueManagement;

public static class QueueManagementRegistrationExtensions
{
    public static IServiceCollection AddQueueManagementBatch7Module(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("QueueManagementDb")
            ?? configuration.GetConnectionString("SoftphoneDb")
            ?? "Server=(localdb)\\mssqllocaldb;Database=pbx-crm;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

        services.AddDbContext<PBXDbContext>(options =>
        {
            options.UseSqlServer(connectionString);
        });

        services.AddScoped<QueueConfigurationRepository>();
        services.AddScoped<IQueueRepository>(sp => sp.GetRequiredService<QueueConfigurationRepository>());
        services.AddScoped<IExtensionRepository>(sp => sp.GetRequiredService<QueueConfigurationRepository>());
        services.AddScoped<IQueueAgentRepository>(sp => sp.GetRequiredService<QueueConfigurationRepository>());

        services.AddScoped<QueueRuntimeRepository>();
        services.AddScoped<IQueueCallRepository>(sp => sp.GetRequiredService<QueueRuntimeRepository>());
        services.AddScoped<IQueueCallEventRepository>(sp => sp.GetRequiredService<QueueRuntimeRepository>());
        services.AddScoped<IQueueCallHistoryRepository>(sp => sp.GetRequiredService<QueueRuntimeRepository>());
        services.AddScoped<IQueueAgentActivityRepository>(sp => sp.GetRequiredService<QueueRuntimeRepository>());
        services.AddScoped<IQueueOutboxRepository>(sp => sp.GetRequiredService<QueueRuntimeRepository>());
        services.AddScoped<IQueueCheckpointRepository>(sp => sp.GetRequiredService<QueueRuntimeRepository>());

        services.AddQueueXapiInfrastructure();
        services.AddQueueManagementBatch5Ingestion(configuration);
        services.AddQueueManagementApplication(configuration);

        services.Replace(ServiceDescriptor.Singleton<IQueueHubMessagePublisherTransport, QueueHubSignalrMessagePublisherTransport>());

        services.AddSwaggerGen(options =>
        {
            var bearerScheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Enter a JWT bearer token.",
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = JwtBearerDefaults.AuthenticationScheme
                }
            };

            options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, bearerScheme);
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [bearerScheme] = Array.Empty<string>()
            });
        });
        services.AddHealthChecks();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("QueueApi", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        return services;
    }

    public static WebApplication UseQueueManagementBatch7Module(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseRateLimiter();

        app.MapHealthChecks("/health");
        app.MapHub<QueueHub>("/hubs/queue");

        return app;
    }
}
