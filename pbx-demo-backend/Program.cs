using System.Text;
using CallControl.Api.Domain;
using CallControl.Api.Hubs;
using CallControl.Api.Infrastructure;
using CallControl.Api.Infrastructure.QueueManagement;
using CallControl.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("softphone.config.json", optional: true, reloadOnChange: true);

builder.Services.Configure<SoftphoneOptions>(builder.Configuration.GetSection(SoftphoneOptions.SectionName));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
});
builder.Services.AddHttpClient();
builder.Services.AddDbContextFactory<PBXDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("SoftphoneDb")
        ?? "Server=(localdb)\\mssqllocaldb;Database=pbx-crm;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";
    options.UseSqlServer(connectionString);
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("SoftphoneCors", policy =>
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var softphoneOptions = builder.Configuration.GetSection(SoftphoneOptions.SectionName).Get<SoftphoneOptions>()
    ?? throw new InvalidOperationException("Softphone options are not configured.");

var keyBytes = Encoding.UTF8.GetBytes(softphoneOptions.JwtSigningKey);
var signingKey = new SymmetricSecurityKey(keyBytes);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = softphoneOptions.JwtIssuer,
            ValidAudience = softphoneOptions.JwtAudience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrWhiteSpace(accessToken)
                    && (path.StartsWithSegments("/hubs/softphone")
                        || path.StartsWithSegments("/hubs/queue")))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SupervisorOnly", policy => policy.RequireRole(AppUserRoles.Supervisor));
});
builder.Services.AddQueueManagementBatch7Module(builder.Configuration);

builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<UserDirectoryService>();
builder.Services.AddSingleton<ThreeCxConfigurationClient>();
builder.Services.AddSingleton<CrmManagementService>();
builder.Services.AddSingleton<DatabaseBootstrapper>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<ThreeCxClientFactory>();
builder.Services.AddSingleton<SessionRegistry>();
builder.Services.AddSingleton<SessionPresenceRegistry>();
builder.Services.AddSingleton<EventDispatcher>();
builder.Services.AddSingleton<CallCdrService>();
builder.Services.AddSingleton<CallManager>();
builder.Services.AddSingleton<WebRtcCallManager>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<SipConfigurationService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var bootstrapper = scope.ServiceProvider.GetRequiredService<DatabaseBootstrapper>();
    await bootstrapper.InitializeAsync(CancellationToken.None);
}

app.UseMiddleware<AppExceptionMiddleware>();
app.UseQueueManagementBatch7Module();
app.UseCors("SoftphoneCors");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<SoftphoneHub>("/hubs/softphone");

app.Run();
