using System.Text;
using CallControl.Api.Domain;
using CallControl.Api.Hubs;
using CallControl.Api.Infrastructure;
using CallControl.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SoftphoneOptions>(builder.Configuration.GetSection(SoftphoneOptions.SectionName));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
});
builder.Services.AddHttpClient();

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
                if (!string.IsNullOrWhiteSpace(accessToken) && path.StartsWithSegments("/hubs/softphone"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<ThreeCxClientFactory>();
builder.Services.AddSingleton<SessionRegistry>();
builder.Services.AddSingleton<SessionPresenceRegistry>();
builder.Services.AddSingleton<EventDispatcher>();
builder.Services.AddSingleton<CallManager>();
builder.Services.AddSingleton<WebRtcCallManager>();
builder.Services.AddSingleton<AuthService>();

var app = builder.Build();

app.UseMiddleware<AppExceptionMiddleware>();
app.UseCors("SoftphoneCors");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<SoftphoneHub>("/hubs/softphone");

app.Run();
