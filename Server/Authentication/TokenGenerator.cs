using Microsoft.IdentityModel.Tokens;
using Server.Authentication.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Server.Authentication
{
    public static class TokenGenerator
    {
        public static string GenerateToken(AppUser user, string secret, TimeSpan? expires = null)
        {
            var handler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim("id", user.Id),
            });

            if (user.Roles is not null && user.Roles.Any()) 
            {
                foreach (var role in user.Roles)
                {
                    var dbName = role.Database.Replace(".db", "");

                    identity.AddClaim(new Claim(ClaimTypes.Role, $"{dbName}:{role.Role}"));
                }
            }
           
            var token = handler.CreateToken(new SecurityTokenDescriptor
            {
                Subject = identity,
                Expires = DateTime.UtcNow.Add(expires ?? TimeSpan.FromMinutes(5)),
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature),
                Audience = "sqlite.user",
                Issuer = "sqlite.authentication"
            });

            return handler.WriteToken(token);
        }
    }
}
