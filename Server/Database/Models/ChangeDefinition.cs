using Server.Authentication.Models;
using System.Data.SQLite;

namespace Server.Database.Models
{
	public record ChangeDefinition(UpdateEventType Id, string Name);
}
