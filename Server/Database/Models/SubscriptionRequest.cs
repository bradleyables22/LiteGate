using System.ComponentModel.DataAnnotations;
using System.Data.SQLite;

namespace Server.Database.Models
{
	public class SubscriptionRequest
	{
		[StringLength(250, MinimumLength = 1)]
		[Url]
		public string Url { get; set; } = string.Empty;
		[StringLength(250, MinimumLength = 1)]
		public string Database { get; set; } = string.Empty;
		[StringLength(250, MinimumLength = 1)]
		public string Table { get; set; } = string.Empty;
		public UpdateEventType Event { get; set; }
	}
}
