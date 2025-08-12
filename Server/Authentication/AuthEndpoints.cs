using Microsoft.AspNetCore.Mvc;
using Server.Authentication.Models;
using Server.Services;

namespace Server.Authentication
{
    public static class AuthEndpoints
    {
        public static WebApplication MapAuthEndpoints(this WebApplication app) 
        {

            var authGroup = app.MapGroup("api/v1/identity").WithTags("Identity");

            authGroup.MapPost("/authenticate", async ([FromBody] UserCredentials credentials,UserDatabase _db,ServerSettings _settings) =>
            {
                var userResult = await _db.GetUserByNameAsync(credentials.UserName);

                if (!userResult.Success)
                    return Results.Unauthorized();

                if (userResult.Data is null || userResult.Data.DisabledAt is not null || !PasswordHasher.VerifyPassword(credentials.Password,userResult.Data.PasswordHash))
                    return Results.Unauthorized();
                var token = TokenGenerator.GenerateToken(userResult.Data, _settings.Secret, TimeSpan.FromMinutes(_settings.TokenExpiryMinutes));
                return Results.Text(token);
            })
            .AllowAnonymous()
            .Produces(200)
            .Accepts<UserCredentials>("application/json")
            .WithOpenApi()
            .WithDisplayName("Authenticate")
            .WithName("Authenticate")
            .WithDescription("Authenticate to the sqlite server via user credentials")
            .WithSummary("Authenticate")
            ;

            return app;
        }
    }
}
