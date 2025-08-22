using Microsoft.AspNetCore.SignalR;
using Server.Authentication.Models;
using System.Security.Claims;

namespace Server.Services
{
    public static class AccessManager
    {
        private static readonly HashSet<string> RestrictedSingles = new(StringComparer.OrdinalIgnoreCase)
		{
			"pragma","vacuum","analyze","reindex",
			"attach","detach","backup",
			"begin","commit","rollback","savepoint","release"
		};

        private static readonly HashSet<string> WriteSingles = new(StringComparer.OrdinalIgnoreCase)
		{
			"insert","update","delete","replace"
		};

        private static readonly (string a, string b)[] SchemaPairs =
        {
			("create","table"),
			("create","index"),
			("create","view"),
			("create","trigger"),
			("create","virtual"), 
			("drop","table"),
			("drop","index"),
			("drop","view"),
			("drop","trigger"),
			("alter","table")
		};

        public static bool MeetsPermissionRequirements(this List<SystemRole>? roles, SystemRole minRequirement) 
        {
            try
            {
                if (roles == null || !roles.Any())
                    return false;

                switch (minRequirement)
                {
                    case SystemRole.admin:
                        return roles.Any(x => x == SystemRole.admin);
                    case SystemRole.editor:
                        return roles.Any(x => x == SystemRole.admin || x == SystemRole.editor);
                    case SystemRole.viewer:
                        return roles.Any(x => x == SystemRole.admin || x == SystemRole.editor || x == SystemRole.viewer);
                    default:
                        return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }


        public static bool HasWritePermissions(this HttpContext context ,string db) 
        {
			bool allowed = false;

			try
			{
				if (Convert.ToBoolean(context.User?.Identity?.IsAuthenticated) == false)
					return allowed;

				var roles = context.User.Claims.Where(x => x.Type == ClaimTypes.Role).Where(x=>x.Value.Contains(db.ToLower()) || x.Value.Contains("*")).ToList();

				if (roles == null || !roles.Any())
					return allowed;

				var accessLevels = roles.Select(x => x.Value.Split(":").LastOrDefault()).ToList();

                if (accessLevels == null || !accessLevels.Any())
                    return allowed;

				allowed = accessLevels.Where(x => x == SystemRole.admin.ToString() || x == SystemRole.editor.ToString()).Any();

                return allowed;
			}
			catch (Exception e)
			{
				return allowed;
			}
        }

		public static List<SystemRole> ExtractAllowedPermissions(this HttpContext context, string db)
		{


			List<SystemRole> allowedRoles = new();
			try
			{
				if (Convert.ToBoolean(context.User?.Identity?.IsAuthenticated) == false)
					return allowedRoles;

				var roles = context.User.Claims.Where(x => x.Type == ClaimTypes.Role).Where(x => x.Value.Contains(db.ToLower()) || x.Value.Contains("*")).ToList();

				if (roles == null || !roles.Any())
					return allowedRoles;

				var accessLevels = roles.Select(x => x.Value.Split(":").LastOrDefault()).ToList();

				if (accessLevels == null || !accessLevels.Any())
					return allowedRoles;

                allowedRoles = accessLevels
					 .Select(s => Enum.TryParse<SystemRole>(s?.Trim(), ignoreCase: true, out var role) && Enum.IsDefined(typeof(SystemRole), role) ? (SystemRole?)role : null)
					 .Where(r => r.HasValue)
					 .Select(r => r.Value)
					 .ToList();

                return allowedRoles;
			}
			catch (Exception)
			{
				return allowedRoles;
			}
		}

        private static List<string> Normalize(IEnumerable<string> tokens) =>
        tokens.Where(s => !string.IsNullOrWhiteSpace(s))
              .Select(s => s.Trim().ToLowerInvariant())
              .ToList();

        private static bool ContainsAny(HashSet<string> set, List<string> tokens)
            => tokens.Any(set.Contains);

        private static bool ContainsPair(List<string> tokens, string first, string second)
        {
            foreach (var t in tokens)
            {
                if (t == first)
                {
                    foreach (var u in tokens.Skip(tokens.IndexOf(t) + 1))
                    {
                        if (u == second) return true;
                    }
                }
            }
            return false;
        }

        private static bool ContainsAnyPair(List<string> tokens, (string a, string b)[] pairs)
            => pairs.Any(p => ContainsPair(tokens, p.a, p.b));

        public static bool ContainsRestrictedTokens(this List<string> tokens)
        {
            try
            {
                var t = Normalize(tokens);
                if (t.Count == 0) 
                    return false;

                if (ContainsAny(RestrictedSingles, t)) 
                    return true;

                return false;
            }
            catch
            {
                return true;
            }
        }

        public static SystemRole MinimalAccessRequired(this List<string> tokens)
        {
            try
            {
                var t = Normalize(tokens);
                if (t.Count == 0) 
                    return SystemRole.viewer;

                if (ContainsAnyPair(t, SchemaPairs))
                    return SystemRole.admin;

                if (ContainsAny(WriteSingles, t))
                    return SystemRole.editor;

                return SystemRole.viewer;
            }
            catch
            {
                return SystemRole.admin;
            }
        }

    }
}
