# HostedSqlite – Phase 1

Lightweight, self‑hosted service that gives each authenticated user their own SQLite databases with an HTTP API for management and SQL execution.

## Phase 1 Scope

- **User Management**: registration, authentication, enabling/disabling users, role assignments.
- **Database Management**: creation, listing, deletion, and export of per‑user SQLite databases.
- **Raw SQL Interaction**: executing non‑query and query statements against user databases.

---

## Architecture Overview

- **Backend**: ASP.NET Core (Minimal APIs)
- **Storage**: SQLite (one file per database)
- **Auth**: JWT bearer tokens with role‑based authorization
- **Concurrency Control**: `SemaphoreSlim` per database to serialize writes
- **Transaction Handling**: every statement wrapped in a transaction
- **API Docs**: OpenAPI + Scalar UI

---

## API Endpoints (Phase 1)

### 1. Identity & Authentication

**POST /api/v1/identity/authenticate**

- Body: `{ "userName": "string", "password": "string" }`
- Returns: JWT token if valid.

### 2. User Management

**GET /api/v1/usermanagement**

- Lists all users (admin only).

**POST /api/v1/usermanagement**

- Create new user (admin only).

**POST /api/v1/usermanagement/{id}/disable**

- Disable a user (admin only).

**POST /api/v1/usermanagement/{id}/enable**

- Enable a user (admin only).

**POST /api/v1/rolemanagement**

- Assign a role to a user (admin only).

### 3. Database Management

**GET /api/v1/databasemanagement**

- List all databases.

**POST /api/v1/databasemanagement**

- Create a new database.

**DELETE /api/v1/databasemanagement/{name}**

- Delete a database.

**GET /api/v1/databasemanagement/export/{name}**

- Download/export a database file.

### 4. Raw SQL Interaction

**POST /api/v1/database/execute/raw/{name}**
- Content-Type: `application/sql`
- Body: raw SQL string (e.g., `INSERT`, `UPDATE`, `DELETE`, `CREATE TABLE`).
- Returns: affected row count (0 for DDL is normal).

**POST /api/v1/database/query/raw/{name}**
- Content-Type: `application/sql`
- Body: raw SQL string (e.g., `SELECT * FROM table`).
- Returns: list of result rows as JSON objects.

---

## Important Implementation Details
- **Role Scoping**: roles can be global (e.g., `*:admin`) or DB‑specific (e.g., `app.db:admin`).
- **Safe DB Names**: validated to prevent path traversal.
- **Error Model**: `TryResult<T>` with `ok`, `value`, and `error` fields.
- **WAL Mode**: enabled on user DBs for better concurrency.

---

## Getting Started
1. Install .NET SDK (matching project target).
2. Clone the repository.
3. Configure `appsettings.Development.json` with JWT secret and data root path.
4. Build and run:
```bash
dotnet restore
dotnet run --project Server
```
---
5. Access API docs at `http://localhost:<port>/scalar/v1`.

---

## Example Usage
**Create DB, add table, insert, query**:
```bash
# Create DB
token=... # from authentication
curl -X POST -H "Authorization: Bearer $token" -H "Content-Type: application/json" \
  -d '{"name":"inventory"}' http://localhost:5000/api/v1/databasemanagement

# Create table (application/sql)
echo "CREATE TABLE IF NOT EXISTS Items(Id TEXT PRIMARY KEY, Name TEXT)" | \
  curl -X POST -H "Authorization: Bearer $token" -H "Content-Type: application/sql" \
  --data-binary @- http://localhost:5000/api/v1/database/execute/raw/inventory

# Insert data
echo "INSERT INTO Items VALUES ('1','Widget')" | \
  curl -X POST -H "Authorization: Bearer $token" -H "Content-Type: application/sql" \
  --data-binary @- http://localhost:5000/api/v1/database/execute/raw/inventory

# Query data
echo "SELECT * FROM Items" | \
  curl -X POST -H "Authorization: Bearer $token" -H "Content-Type: application/sql" \
  --data-binary @- http://localhost:5000/api/v1/database/query/raw/inventory
```
