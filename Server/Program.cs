using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Server.Authentication;
using Server.Interaction;
using Server.Management.Server;
using Server.Management.User;
using Server.Services;
using Server.Transformers;
using System.Net;
using System.Text;
using System.Threading.Channels;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

SQLitePCL.Batteries.Init();

builder.Services.AddHttpClient("webhooks", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Sqlite-Subscriptions/1.0");
});
builder.Services.AddHostedService<ChangeEventProcessor>();
builder.Services.AddSingleton<Channel<SqliteChangeEvent>>(_ =>
{
    var options = new BoundedChannelOptions(capacity: 10000)
    {
        SingleReader = true, // Change Event Processor
        SingleWriter = true, //Gate Manager
        AllowSynchronousContinuations = true,
        FullMode = BoundedChannelFullMode.Wait,
    };

    return Channel.CreateBounded<SqliteChangeEvent>(options);
});


builder.Services.AddSingleton<UserDatabase>();
builder.Services.AddSingleton<ServerSettings>();
builder.Services.AddSingleton<DatabaseGateManager>();
builder.Services.AddHostedService<SecretRotator>();

builder.Services
	.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
	.AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
	.Configure<ServerSettings>((options, settings) =>
	{
		options.TokenValidationParameters = new TokenValidationParameters
		{
			ValidateIssuerSigningKey = true,
			ValidateIssuer = true,
			ValidIssuer = "sqlite.authentication",
			ValidateAudience = true,
			ValidAudience = "sqlite.user",
			RequireSignedTokens = true,
			ClockSkew = TimeSpan.Zero,

			IssuerSigningKeyResolver = (token, securityToken, kid, validationParameters) =>
			{
                string secret = settings.GetSecretAsync().GetAwaiter().GetResult();
				var keyBytes = Encoding.UTF8.GetBytes(secret ?? string.Empty);
				return new[] { new SymmetricSecurityKey(keyBytes) };
			}
		};
	});


builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy("all",
        builder =>
        {
            builder.AllowAnyOrigin();
            builder.AllowAnyHeader();
            builder.AllowAnyMethod();
            builder.WithExposedHeaders(
                "X-Webhook-Id",
                "X-Webhook-Timestamp",
                "X-Webhook-Signature",
                "X-Idempotency-Key",
                "X-Webhook-Retry"
            );
        });

});

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, IPAddress>(context =>
    {
        var remoteIp = context.Connection.RemoteIpAddress ?? IPAddress.None;
        return RateLimitPartition.GetFixedWindowLimiter(remoteIp, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 10
        });
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddDocumentTransformer<TitleTransformer>();
});

builder.Services.AddHostedService<SystemSeeder>();

var app = builder.Build();

app.UseHsts();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseCors("all");

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi("/openapi/v1.json");
app.MapScalarApiReference(options =>
{
    options.Title = "LiteGate SQLite API";
    options.Theme = ScalarTheme.BluePlanet;
    options.ForceThemeMode = ThemeMode.Dark;
    options.WithDarkModeToggle(false);
});

app.MapAuthEndpoints();
app.MapUserManagementEndpoints();
app.MapUserRoleManagementEndpoints();
app.MapDatabaseManagementEndpoints();
app.MapDatabaseInteractionEndpoints();
app.MapWebHookEndpoints();

app.Run();
