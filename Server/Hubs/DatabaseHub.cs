using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Server.Authentication.Models;
using Server.Extensions;
using Server.Hubs.Models;
using Server.Services;
using System.Data;
using System.Dynamic;
using System.Runtime.CompilerServices;

namespace Server.Hubs
{
    [Authorize]
    public class DatabaseHub : Hub
    {

        public void ExecuteAsync() { }

        [Authorize]
        public async IAsyncEnumerable<dynamic> QueryAsync(QueryRequest request, [EnumeratorCancellation] CancellationToken ct) 
        {
            request.Database = request.Database.Replace(".db", "");

            var tokens = request.Query.Split(" ").ToList();

            var restricted = tokens.ContainsRestrictedTokens();
            if (restricted)
            {
                await Clients.Caller.SendAsync("Restricted statement identified", "403");
                Context.Abort();
            }

            var exists = DirectoryManager.DatabaseFileExists(request.Database);

            if (!exists)
            {
                await Clients.Caller.SendAsync("Database not found", "404");
                Context.Abort();
            }

            var securityRequirement = tokens.MinimalAccessRequired();

            if (securityRequirement != SystemRole.viewer)
            {
                await Clients.Caller.SendAsync("Query tokens indicate elevated security requirements, please use ExectuteAsync if this was intentional", "403");
                Context.Abort();
            }

            var userPermissions = Context.ExtractAllowedPermissions(request.Database);

            if (userPermissions is null || !userPermissions.Any())
            {
                await Clients.Caller.SendAsync("Access requirement not met", "403");
                Context.Abort();
            }
            if (request.Query.AsSpan().Trim().IndexOf(';') >= 0)
            {
                await Clients.Caller.SendAsync("Multiple statements are not allowed", "403");
                Context.Abort();
            }

            var connectionString = DirectoryManager.BuildSqliteConnectionString(request.Database, true);
            using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = request.Query;
            cmd.CommandTimeout = Convert.ToInt32(request.Timeout);

            if (request.Params is not null && request.Params.Any())
            {
                foreach (var (name, raw) in request.Params)
                {
                    var value = ApiExtensions.Normalize(raw); 
                    var p = new SqliteParameter(name, value);

                    if (value is long) p.DbType = System.Data.DbType.Int64;
                    else if (value is int) p.DbType = System.Data.DbType.Int32;
                    else if (value is double) p.DbType = System.Data.DbType.Double;
                    else if (value is bool) p.DbType = System.Data.DbType.Boolean;
                    else if (value is DateTimeOffset) p.DbType = System.Data.DbType.DateTimeOffset;
                    else if (value is DateTime) p.DbType = System.Data.DbType.DateTime;

                    cmd.Parameters.Add(p);
                }
            }

            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);

            var fieldCount = reader.FieldCount;
            var colIndexes = Enumerable.Range(0, fieldCount).ToArray();
            var colNames = colIndexes.Select(reader.GetName).ToArray();

            while (await reader.ReadAsync(ct))
            {
                ct.ThrowIfCancellationRequested();

                var row = new ExpandoObject() as IDictionary<string, object?>;

                foreach (var i in colIndexes)
                {
                    var name = colNames[i];
                    object? raw = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i);

                    row[ApiExtensions.EnsureUniqueKey(name, row)] = ApiExtensions.NormalizeForJson(raw);
                }

                yield return (ExpandoObject)row;
            }
        }
    }
}
