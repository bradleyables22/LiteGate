using Server.Authentication.Models;
using Server.Database.Models;
using System.Data.SQLite;

namespace Server.Database
{
	public static class WebHookEndpoints
	{
		public static WebApplication MapWebHookEndpoints(this WebApplication app) 
		{

			var webhooksGroup = app.MapGroup("api/v1/subscriptions").WithTags("Webhook Subscriptions");

			webhooksGroup.MapGet("/types", () =>
			{
				var types = Enum.GetValues<UpdateEventType>()
					.Select(t => new ChangeDefinition(
						Id: t,
						Name: System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(t.ToString())
					));

				return Results.Ok(types);
			})
			.RequireAuthorization(policy => policy.RequireRole("*:admin", "*:editor", "*:viewer", "app.db:admin", "app.db:editor", "app.db:viewer"))
			.Produces<List<ChangeDefinition>>()
			.WithOpenApi()
			.WithDisplayName("GetTypes")
			.WithName("GetTypes")
			.WithDescription("Get all requestable change types")
			.WithSummary("Get Event Types")
			;


			return app;
		}



	}
}
