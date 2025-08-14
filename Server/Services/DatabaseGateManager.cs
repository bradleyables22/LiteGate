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

        public async Task<TryResult<long>> ExecuteAsync(SqlRequest request) 
        {
            var _lock = GetLockForDatabase(request.Database);

            await _lock.WaitAsync();

            try
            { 
                var connectionString = DirectoryManager.BuildSqliteConnectionString(request.Database, readOnly: false);
                using var conn = new SqliteConnection(connectionString);
                await conn.OpenAsync();
                await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();

                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = request.Statement;
                cmd.CommandTimeout = Convert.ToInt32(request.Timeout);

                var rows = await cmd.ExecuteNonQueryAsync();
                await tx.CommitAsync();

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

        public async Task<TryResult<List<ExpandoObject>>> QueryAsync(SqlRequest request) 
        {
            try
            {
                var connectionString = DirectoryManager.BuildSqliteConnectionString(request.Database, true);
                using var conn = new SqliteConnection(connectionString);
                await conn.OpenAsync();

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = request.Statement;
                cmd.CommandTimeout = Convert.ToInt32(request.Timeout);

                await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);

                var fieldCount = reader.FieldCount;
                var columnIndexes = Enumerable.Range(0, fieldCount).ToArray();
                var columnNames = columnIndexes.Select(reader.GetName).ToArray();

                List<ExpandoObject> results = new List<ExpandoObject>();
                int iterationMax = 5000;
                int iterationCount = 0;
                while (await reader.ReadAsync())
                {

                    var row = new ExpandoObject() as IDictionary<string, object?>;

                    foreach (var columnIndex in columnIndexes)
                    {
                        if (iterationCount >= iterationMax)
                            break;

                        try
                        {
                            var name = columnNames[columnIndex];
                            object? raw = await reader.IsDBNullAsync(columnIndex) ? null : reader.GetValue(columnIndex);

                            row[ApiExtensions.EnsureUniqueKey(name, row)] = ApiExtensions.NormalizeForJson(raw);

                            iterationCount++;
                        }
                        catch (Exception)
                        {
                            continue;
                        }
                        
                    }

                    results.Add((ExpandoObject)row);
                }
                return TryResult<List<ExpandoObject>>.Pass(results);
            }
            catch (Exception e)
            {
                return TryResult<List<ExpandoObject>>.Fail(e.Message, e);
            }
        }
    }
}
