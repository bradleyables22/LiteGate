using Microsoft.AspNetCore.Mvc;
using Server.Authentication.Models;
using Server.Database.Models;
using Server.Services;
using Server.Utilities;
using System.Data.SQLite;
using System.Security.Claims;
using static System.Net.WebRequestMethods;

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
			.RequireAuthorization()
			.Produces<List<ChangeDefinition>>()
			.WithOpenApi()
			.WithDisplayName("GetTypes")
			.WithName("GetTypes")
			.WithDescription("Get all requestable change types")
			.WithSummary("Get Event Types")
			;

            webhooksGroup.MapGet("", async (UserDatabase _db,long skip = 0, int take = 10) =>
            {
				var result = await _db.GetSubscriptionsAsync(skip, take);

				return result.ToResult();
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "app.db:admin"))
            .Produces<OffsetTryResult<SubscriptionRecord>>(200)
            .WithOpenApi()
            .WithDisplayName("SubscriptionsPaged")
            .WithName("SubscriptionsPaged")
            .WithDescription("Get all subscriptions by offset")
            .WithSummary("Get Subscriptions")
            ;

            webhooksGroup.MapGet("/{userId}", async (UserDatabase _db, string userId, long skip = 0, int take = 10) =>
            {
				var result = await _db.GetSubscriptionsByUserAsync(userId,skip, take);

                return result.ToResult();
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "app.db:admin"))
            .Produces<OffsetTryResult<SubscriptionRecord>>(200)
            .WithOpenApi()
            .WithDisplayName("SubscriptionsByUser")
            .WithName("SubscriptionsByUser")
            .WithDescription("Get all subscriptions by offset for a user")
            .WithSummary("By User")
            ;

            webhooksGroup.MapGet("/self", async (UserDatabase _db,HttpContext _http, long skip = 0, int take = 10) =>
            {
                var userId = _http.User.FindFirst("id")?.Value;

                if (string.IsNullOrEmpty(userId))
                    return Results.Forbid();

                var result = await _db.GetSubscriptionsByUserAsync(userId, skip, take);

                return result.ToResult();
            })
            .RequireAuthorization()
            .Produces<OffsetTryResult<SubscriptionRecord>>(200)
            .WithOpenApi()
            .WithDisplayName("SubscriptionsSelf")
            .WithName("SubscriptionsSelf")
            .WithDescription("Get all subscriptions by offset for the signed in account")
            .WithSummary("Self")
            ;

            webhooksGroup.MapPost("/subscribe", async (UserDatabase _db, HttpContext _http, [FromBody] SubscriptionRequest request) =>
            {

                


                var userId = _http.User.FindFirst("id")?.Value;

                if (string.IsNullOrEmpty(userId))
                    return Results.Forbid();

                var roles = _http.User.Claims
                    .Where(c => c.Type == ClaimTypes.Role)
                    .Select(c => c.Value)
                    .ToList();

                request.Database = request.Database.Replace(".db", "");
                var exists = DirectoryManager.DatabaseFileExists(request.Database);

                if (!exists)
                    return Results.NotFound();

                var rawDatabaseStrings = roles.Select(x => x.ToLower().Split(":").FirstOrDefault()).ToList();

                var databaseRoles = rawDatabaseStrings.Where(x => x == request.Database || x == "*").ToList();

                if(databaseRoles is null || !databaseRoles.Any())
                    return Results.Forbid();

                var secret = ChangeEventSigner.GenerateSecretBase64();

                var newSubscription = new SubscriptionRecord
                {
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow,
                    Database = request.Database,
                    Table = request.Table,
                    Event = request.Event,
                    Url = request.Url,
                    Secret = secret,
                };

                var createResult = await _db.CreateSubscriptionAsync(newSubscription);

                if (createResult.Success)
                {
                    if (createResult.Data)
                        return Results.Ok(new { 
                            newSubscription.Id,
                            secret = newSubscription.Secret
                        });
                    else
                        return Results.Problem(
                            detail: "Could not create subscription",
                            title: "Failed to create",
                            statusCode: StatusCodes.Status500InternalServerError);
                }
                else
                    return createResult.ToResult();

            })
            .RequireAuthorization()
            .Produces(200)
            .WithOpenApi()
            .WithDisplayName("CreateSubscription")
            .WithName("CreateSubscriptions")
            .WithDescription(@"Register a subscription for a specific database, table and event type 
                for the authenticated user. On success the subscription id and its signing secret are returned.  
                DO NOT LOSE THE SECRET!  
                - The secret is shown only once, at creation. Store it securely (e.g. environment variable, 
                  cloud secret manager, or other secure config).  
                - You will need this secret to verify every webhook call sent to your endpoint.  
                - Each webhook includes a header `X-Webhook-Signature: t=<timestamp>,v1=<hmac>` where `<hmac>` 
                  is computed as HMAC-SHA256 over `""{timestamp}.{body}""` using your secret.  
                - On receipt, recompute the HMAC with your saved secret and reject the request if it doesn’t match 
                  or if the timestamp is older than 5 minutes.
                If you lose the secret you must delete and recreate the subscription.")
            .WithSummary("Subscribe")
            ;

            webhooksGroup.MapDelete("/{recordId}", async (UserDatabase _db, HttpContext _http, string recordId) =>
            {
                var userId = _http.User.FindFirst("id")?.Value;

                if (string.IsNullOrEmpty(userId))
                    return Results.Forbid();

                bool fullAccess = false;

                if (_http.User.IsInRole("*:admin") || _http.User.IsInRole("app.db:admin"))
                    fullAccess = true;
                else
                    fullAccess = false;

                var recordResult = await _db.GetSubscriptionByIdAsync(recordId);
                if (recordResult.Success)
                {
                    bool deleteAllowed = fullAccess;

                    if (recordResult.Data is null)
                        return Results.NotFound();

                    if (!fullAccess)
                    {
                        if (recordResult.Data.UserId == userId)
                            deleteAllowed = true;
                    }

                    if (deleteAllowed) 
                    {
                        var deleteResult = await _db.DeleteSubscriptionAsync(recordId);

                        if (deleteResult.Success)
                        {
                            if (deleteResult.Data)
                                return Results.Ok();
                            else
                                return Results.Problem(
                                    detail: "Could not delete subscription",
                                    title: "Failed to delete",
                                    statusCode: StatusCodes.Status500InternalServerError
                                );
                        }
                        else
                            return deleteResult.ToResult();
                    }
                    else
                        return Results.Ok();
                }
                else
                    return recordResult.ToResult();
            })
            .RequireAuthorization()
            .Produces(200)
            .WithOpenApi()
            .WithDisplayName("DeleteSubscription")
            .WithName("DeleteSubscription")
            .WithDescription("Delete a subscription. Admins may delete any subscription.")
            .WithSummary("Delete")
            ;

            webhooksGroup.MapDelete("/clear/{userid}", async (UserDatabase _db, string userId) =>
            {
                var clearResult = await _db.ClearSubscriptionsByUserAsync(userId);

                if (clearResult.Success)
                    return Results.Ok();

                return clearResult.ToResult();

            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "app.db:admin"))
            .Produces(200)
            .WithOpenApi()
            .WithDisplayName("ClearUserSubscriptions")
            .WithName("ClearUserSubscriptions")
            .WithDescription("Clear all subscriptions for a given user.")
            .WithSummary("Clear")
            ;

            webhooksGroup.MapDelete("/self/clear", async (UserDatabase _db, HttpContext _http) =>
            {
                var userId = _http.User.FindFirst("id")?.Value;

                if (string.IsNullOrEmpty(userId))
                    return Results.Forbid();

                var clearResult = await _db.ClearSubscriptionsByUserAsync(userId);

                if (clearResult.Success)
                    return Results.Ok();

                return clearResult.ToResult();

            })
            .RequireAuthorization()
            .Produces(200)
            .WithOpenApi()
            .WithDisplayName("ClearSubscriptions")
            .WithName("ClearSubscriptions")
            .WithDescription("Clear all subscriptions for the authenticated user.")
            .WithSummary("Clear Self")
            ;


            return app;
		}



	}
}
