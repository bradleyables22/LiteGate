using Microsoft.Data.Sqlite;
using Server.Extensions;
using Server.Utiilites;
using System.Collections.Concurrent;
using System.Data;
using System.Data.SQLite;
using System.Data.SqlTypes;
using System.Dynamic;
using System.Threading.Channels;
using SSQLite = System.Data.SQLite;

namespace Server.Database
{
    public sealed class DatabaseGateManager
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
        private readonly Channel<SqliteChangeEvent> _channel;
        public DatabaseGateManager(Channel<SqliteChangeEvent> channel)
        {
            _channel = channel;
        }

        SemaphoreSlim GetLockForDatabase(string dbName) =>
            _locks.GetOrAdd(dbName.ToLowerInvariant(), _ => new SemaphoreSlim(1, 1));

        public async Task<TryResult<long>> ExecuteAsync(SqlRequest request, CancellationToken ct = default)
        {
            var gate = GetLockForDatabase(request.Database);
            await gate.WaitAsync(ct);

            try
            {
                var cs = DirectoryManager.BuildSqliteConnectionString(request.Database, readOnly: false);

                using var conn = new SSQLite.SQLiteConnection(cs);
                conn.Open(); 

                List<SqliteChangeEvent> changes = new();
                void OnUpdate(object? s, SSQLite.UpdateEventArgs e)
                {
                    if (string.Equals(e.Database, "main", StringComparison.OrdinalIgnoreCase))
                        changes.Add(new SqliteChangeEvent { Database = request.Database, Table = e.Table, RowId = e.RowId , EventType = e.Event});
                }

                conn.Update += OnUpdate;

                using var tx = await conn.BeginTransactionAsync(ct); 
                using var cmd = conn.CreateCommand();
                cmd.Transaction = (SQLiteTransaction)tx;
                cmd.CommandText = request.Statement;
                cmd.CommandTimeout = Convert.ToInt32(request.Timeout);

                var rows = await cmd.ExecuteNonQueryAsync(ct);

                tx.Commit();

                //later only do this if there are actual subscription for the db/table combo
                foreach (var c in changes)
                {
                    await _channel.Writer.WriteAsync(c);
                }

                conn.Update -= OnUpdate;

                if (rows == -1)
                    return TryResult<long>.Fail("SQLite returned -1", new SqlNullValueException());

                return TryResult<long>.Pass(rows);
            }
            catch (Exception e)
            {
                return TryResult<long>.Fail(e.Message, e);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<TryResult<QueryResult>> QueryAsync(SqlRequest request, CancellationToken ct = default)
        {
            try
            {
                const int Limit = 10000;

                var cs = DirectoryManager.BuildSqliteConnectionString(request.Database, readOnly: true);
                await using var conn = new SqliteConnection(cs);
                await conn.OpenAsync(ct);

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = request.Statement;
                cmd.CommandTimeout = Convert.ToInt32(request.Timeout);

                await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);

                var fieldCount = reader.FieldCount;
                var columnIndexes = Enumerable.Range(0, fieldCount).ToArray();
                var columnNames = columnIndexes.Select(reader.GetName).ToArray();

                var rows = new List<ExpandoObject>();
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

                return TryResult<QueryResult>.Pass(data);
            }
            catch (Exception e)
            {
                return TryResult<QueryResult>.Fail(e.Message, e);
            }
        }
    }

}
