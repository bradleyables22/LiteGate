using Microsoft.Data.Sqlite;
using Server.Extensions;
using Server.Interaction.Enums;
using Server.Utilities;
using System.Collections.Concurrent;
using System.Data;
using System.Data.SqlTypes;
using System.Dynamic;
using System.Threading.Channels;
namespace Server.Interaction
{
    public sealed class DatabaseGateManager
    {
        public readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
        private readonly Channel<SqliteChangeEvent> _channel;
        public DatabaseGateManager(Channel<SqliteChangeEvent> channel)
        {
            _channel = channel;
        }

        SemaphoreSlim GetLockForDatabase(string dbName) => _locks.GetOrAdd(dbName.ToLowerInvariant(), _ => new SemaphoreSlim(1, 1));

        public async Task<TryResult<bool>> VacuumAsync(string databaseName, CancellationToken ct = default)
        {
            var gate = GetLockForDatabase(databaseName);
            await gate.WaitAsync(ct);
            try
            {
                var cs = DirectoryManager.BuildSqliteConnectionString(databaseName, readOnly: false);
                using var conn = new SqliteConnection(cs);
                await conn.OpenAsync(ct);

                using var cmd = conn.CreateCommand();

                cmd.CommandText = "VACUUM;";
                await cmd.ExecuteNonQueryAsync(ct);
                cmd.CommandText = "PRAGMA journal_mode = WAL;";
                await cmd.ExecuteScalarAsync(ct);
                return TryResult<bool>.Pass(true);
            }
            catch (Exception ex)
            {
                return TryResult<bool>.Fail("Failed to vacuum database.", ex);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<TryResult<bool>> TruncateWalAsync(string databaseName,CancellationToken ct = default)
        {
            var gate = GetLockForDatabase(databaseName);
            await gate.WaitAsync(ct);
            try
            {
                var cs = DirectoryManager.BuildSqliteConnectionString(databaseName, readOnly: false);
                using var conn = new SqliteConnection(cs);
                await conn.OpenAsync(ct);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                var result = await cmd.ExecuteScalarAsync(ct);

                return TryResult<bool>.Pass(true);
            }
            catch (Exception ex)
            {
                return TryResult<bool>.Fail("Failed to truncate WAL.", ex);
            }
            finally
            {
                gate.Release();
            }
        }
        public async Task<TryResult<bool>> EnsureWalEnabledAsync(string databaseName,CancellationToken ct = default)
        {
            var gate = GetLockForDatabase(databaseName);
            await gate.WaitAsync(ct);

            try
            {
                var cs = DirectoryManager.BuildSqliteConnectionString(databaseName, readOnly: false);
                using var conn = new SqliteConnection(cs);
                await conn.OpenAsync(ct);


				await using var tx = await conn.BeginTransactionAsync(ct);
				using var cmd = conn.CreateCommand();
				cmd.Transaction = (SqliteTransaction)tx;
				cmd.CommandText = "PRAGMA journal_mode = WAL;";

                var result = (await cmd.ExecuteScalarAsync(ct))?.ToString();
                
                if (!string.Equals(result, "wal", StringComparison.OrdinalIgnoreCase))
                    return TryResult<bool>.Fail("Failed to enable WAL mode.", new Exception($"Unexpected journal_mode result: {result}"));

                return TryResult<bool>.Pass(true);
            }
            catch (Exception ex)
            {
                return TryResult<bool>.Fail("Exception occurred while enabling WAL mode.", ex);
            }
            finally { gate.Release(); }
        }


		public async Task<TryResult<long>> ExecuteAsync(SqlRequest request, CancellationToken ct = default)
		{
			var gate = GetLockForDatabase(request.Database);
			await gate.WaitAsync(ct);

			try
			{
                var cs = DirectoryManager.BuildSqliteConnectionString(request.Database);

				await using var conn = new SqliteConnection(cs);
				await conn.OpenAsync(ct);

				var changes = new List<SqliteChangeEvent>();
				using var sub = Server.Interaction.SqliteHooks.RegisterUpdateHook(
					conn,
					(op, dbName, table, rowid) =>
					{
						if (string.Equals(dbName, "main", StringComparison.OrdinalIgnoreCase))
						{
							changes.Add(new SqliteChangeEvent
							{
								Database = request.Database,
								Table = table,
								RowId = rowid,
								EventType = (UpdateEventType)op
							});
						}
					});

				await using var tx = await conn.BeginTransactionAsync(ct);
				await using var cmd = conn.CreateCommand();
				cmd.Transaction = (SqliteTransaction)tx;
				cmd.CommandText = request.Statement;
				cmd.CommandTimeout = Convert.ToInt32(request.Timeout);

				var rows = await cmd.ExecuteNonQueryAsync(ct);
				await tx.CommitAsync(ct);

				foreach (var c in changes)
				{
					await _channel.Writer.WriteAsync(c, ct);
				}

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

        public async Task PrimeNewDatabaseAsync(string databaseName, CancellationToken ct = default) 
        {
            var tableName = Guid.NewGuid();
            var createTableSql = $"CREATE TABLE IF NOT EXISTS _TEST-{tableName}_ (Id INTEGER PRIMARY KEY AUTOINCREMENT,Data TEXT);";
            var querySql = $"Select * from _TEST-{tableName}_;";
            var dropTableSql = $"DROP TABLE IF EXISTS _TEST-{tableName}_;";
            try
            {
                _ = await ExecuteAsync(new SqlRequest { Database = databaseName, Statement = createTableSql });

                _ = await QueryAsync(new SqlRequest { Database = databaseName, Statement = querySql });

                _ = await ExecuteAsync(new SqlRequest { Database = databaseName, Statement = dropTableSql });

                _ = await TruncateWalAsync(databaseName);

                return;
            }
            catch (Exception)
            {

            }
        }
    }

}
