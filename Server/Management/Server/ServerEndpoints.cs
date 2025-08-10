using Server.Services;

namespace Server.Management.Server
{
    public static class ServerEndpoints
    {
        public static WebApplication MapServerSettingsEndpoints(this WebApplication app) 
        {

            var settingsGroup = app.MapGroup("api/v1/server management").WithTags("Server Management");

            settingsGroup.MapPatch("/change/secret", (ServerSettings _st) =>
            {
                _st.GenerateSecret();

                return Results.Ok();
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin","app.db:admin"))
            .Produces(200)
            .WithOpenApi()
            .WithDisplayName("ChangeSecret")
            .WithName("ChangeSecret")
            .WithDescription("Change the JWT Secret")
            .WithSummary("Change Secret")
            ;

            settingsGroup.MapPatch("/change/expiry/{minutes}", (int minutes, ServerSettings _st) =>
            {
                if (minutes < 1)
                    return Results.BadRequest("Minute value must be above 1");

                _st.ChangeTokenExpiry(minutes);

                return Results.Ok();
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "app.db:admin"))
            .Produces(200)
            .WithOpenApi()
            .WithDisplayName("ChangeExpiry")
            .WithName("ChangeExpiry")
            .WithDescription("Change the JWT expiry time in minutes")
            .WithSummary("Change Expiry")
            ;
            return app;
        }
    }
}
