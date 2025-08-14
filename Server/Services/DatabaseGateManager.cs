using Microsoft.Data.Sqlite;
using Server.Extensions;
using Server.Utiilites;
using System.Collections.Concurrent;
using System.Data;
using System.Data.SqlTypes;
using System.Dynamic;

namespace Server.Services
{
    public sealed class DatabaseGateManager
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

        SemaphoreSlim GetLockForDatabase(string dbName)
        {
            return _locks.GetOrAdd(dbName.ToLower(), _ => new SemaphoreSlim(1, 1));
        }

        public async Task<TryResult<long>> ExecuteAsync(SqlRequest request, CancellationToken ct = default) 
        {
            var _lock = GetLockForDatabase(request.Database);

            await _lock.WaitAsync();

            try
            { 
                var connectionString = DirectoryManager.BuildSqliteConnectionString(request.Database, readOnly: false);
                using var conn = new SqliteConnection(connectionString);
                await conn.OpenAsync(ct);
                await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = request.Statement;
                cmd.CommandTimeout = Convert.ToInt32(request.Timeout);

                var rows = await cmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);

                if (rows == -1)
                    return TryResult<long>.Fail("Sqlite return -1 result", new SqlNullValueException());

                return TryResult<long>.Pass(rows);
            }
            catch (Exception e)
            {
                return TryResult<long>.Fail(e.Message, e);
            }
            finally 
            {
                _lock.Release();
            }
        }

        public async Task<TryResult<QueryResult>> QueryAsync(SqlRequest request, CancellationToken ct = default)
        {
            try
            {
                const int Limit = 10000;

                var connectionString = DirectoryManager.BuildSqliteConnectionString(request.Database, readOnly: true);
                await using var conn = new SqliteConnection(connectionString);
                await conn.OpenAsync(ct);

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = request.Statement;
                cmd.CommandTimeout = Convert.ToInt32(request.Timeout);

                await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);

                var fieldCount = reader.FieldCount;
                var columnIndexes = Enumerable.Range(0, fieldCount).ToArray();
                var columnNames = columnIndexes.Select(reader.GetName).ToArray();

                var rows = new List<ExpandoObject>(Math.Min(Limit, 256));
                bool hitLimit = false;

                while (await reader.ReadAsync(ct))
                {
                    if (rows.Count >= Limit)
                    {
                        hitLimit = true;
                        break;
                    }

                    var row = new ExpandoObject() as IDictionary<string, object?>;

                    foreach (var i in columnIndexes)
                    {
                        try
                        {
                            var name = columnNames[i];
                            object? raw = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i);
                            row[ApiExtensions.EnsureUniqueKey(name, row)] = ApiExtensions.NormalizeForJson(raw);
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    rows.Add((ExpandoObject)row);
                }

                var data = new QueryResult
                {
                    Items = rows,
                    TotalReturned = rows.Count,
                    Limit = Limit,
                    HitLimit = hitLimit
                };

                return new TryResult<QueryResult> { Success = true, Data = data };
            }
            catch (Exception e)
            {
                return TryResult<QueryResult>.Fail(e.Message, e);
            }
        }
    }
}
