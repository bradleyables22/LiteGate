using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Server.Authentication;
using Server.Authentication.Models;
using Server.Database;
using Server.Management.Server;
using Server.Management.User;
using Server.Services;
using Server.Transformers;
using Server.Utilities;
using System.Net;
using System.Text;
using System.Threading.Channels;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

SQLitePCL.Batteries.Init();


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
				var keyBytes = Encoding.UTF8.GetBytes(settings.Secret ?? string.Empty);
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
    //this is kind of annoying to have to have, swagger did it better
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddDocumentTransformer<TitleTransformer>();
});

var app = builder.Build();

DirectoryManager.EnsureDatabaseFolder("app");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<UserDatabase>();
    
    await db.EnsureTablesExistAsync();
    await db.EnsureWalEnabledAsync();
    const string defaultUserName = "SuperAdmin";
    const string defaultPassword = "ChangeDisPassword123!";

    var userExistsResult = await db.UserExistsAsync(defaultUserName);

    if (!userExistsResult.Data)
    {
        var user = new AppUser
        {
            UserName = defaultUserName,
            Roles = new List<DatabaseRole>
            {
                new() { Database = "*", Role = SystemRole.admin }
            }
        };
        await db.CreateUserAsync(user, defaultPassword);
    }

}

app.UseHsts();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseCors("all");

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi("/openapi/v1.json");
app.MapScalarApiReference(options =>
{
    options.Title = "Hosted Sqlite API";
    options.Theme = ScalarTheme.BluePlanet;
    options.ForceThemeMode = ThemeMode.Dark;
    options.WithDarkModeToggle(false);
});

app.MapAuthEndpoints();
app.MapUserManagementEndpoints();
app.MapUserRoleManagementEndpoints();
app.MapServerSettingsEndpoints();
app.MapDatabaseManagementEndpoints();
app.MapDatabaseInteractionEndpoints();

app.Run();
