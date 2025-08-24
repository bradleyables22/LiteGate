using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Server.Interaction
{
	public static class SqliteHooks
	{
		public static IDisposable RegisterUpdateHook(SqliteConnection conn,Action<int, string, string, long> onChange)
		{
			if (conn.State != System.Data.ConnectionState.Open)
				conn.Open();

			sqlite3 db = conn.Handle;

			delegate_update del = (user_data, type, database, table, rowid) =>
			{
				string dbName = database.utf8_to_string(); 
				string tblName = table.utf8_to_string();   
				onChange(type, dbName, tblName, rowid);
			};

			raw.sqlite3_update_hook(db, del, null);

			return new UpdateHookSubscription(db, del);
		}

		private sealed class UpdateHookSubscription : IDisposable
		{
			private readonly sqlite3 _db;
			private delegate_update? _del;
			private bool _disposed;

			public UpdateHookSubscription(sqlite3 db, delegate_update del)
			{
				_db = db;
				_del = del; // strong ref prevents GC
			}

			public void Dispose()
			{
				if (_disposed) 
					return;

				_disposed = true;

				raw.sqlite3_update_hook(_db, (delegate_update?)null, null);
				_del = null;
			}
		}
	}
}
