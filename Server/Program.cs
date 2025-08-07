
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Server.Authentication;
using Server.Management.User;
using Server.Services;
using Server.Transformers;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

SQLitePCL.Batteries.Init();
builder.Services.AddSingleton<UserDatabase>();
builder.Services.AddSingleton<ServerSettings>();


builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
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
                //dont want to restart the server to change secret (you'd lose all data) so need to dynamically pull from the singleton settings
                var provider = builder.Services.BuildServiceProvider();
                var serverSettings = provider.GetRequiredService<ServerSettings>();

                var key = Encoding.UTF8.GetBytes(serverSettings.Secret);
                return new[] { new SymmetricSecurityKey(key) };
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
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

var app = builder.Build();

Directory.CreateDirectory("Data");

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
app.UseCors();

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
app.Run();
