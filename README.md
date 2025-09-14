# LiteGate

LiteGate is a lightweight, self‑hosted HTTP API for hosting and interacting with **SQLite** databases. It provides straightforward endpoints to create/manage databases, run SQL, and subscribe to change events via **HMAC‑signed webhooks**.

**Highlights**
> - Multi‑database hosting: can be scoped per‑user or shared
> - Raw SQL over HTTP (`application/sql`)
> - JWT auth with role claims (e.g., `*:admin`, `app:admin`)
> - HMAC‑signed webhook notifications on DB changes
> - WAL mode is **always enforced** for durability and concurrency
> - Vacuum, checkpoint truncate, download, create, delete endpoints

---

## Quickstart

### Prerequisites
- .NET 9 SDK

### Run the server
```bash
# Clone your repo (example):
# git clone https://github.com/your-org/litegate.git
cd litegate/Server

# Run
dotnet run
```

On first boot, LiteGate seeds a default admin user:
- **User**: `SuperAdmin`
- **Password**: `ChangeDisPassword123!`

Use these credentials to authenticate and obtain a JWT (see **Identity** endpoints below), then change the password or (ideally) create a new user and burn the seeded account.

> **Note:** LiteGate does **not** use `appsettings.json` for configuration in this build.

---

## Authentication & Roles
LiteGate issues JSON Web Tokens (JWTs). Many endpoints require roles like `*:admin` or `app:admin`. Include the token in `Authorization: Bearer <token>`.

---

## API Reference

All endpoints are rooted under `api/v1/...`. Routes are grouped below by area. Each route lists: **method** — **path** — **auth** — **purpose** — **request** — **response** — **examples**.

### 1) Identity
**POST — `/api/v1/identity/authenticate`**
- **Auth**: No (login)
- **Purpose**: Exchange username/password for a JWT.
- **Request (JSON)**:
  ```json
  { "userName": "SuperAdmin", "password": "ChangeDisPassword123!" }
  ```
- **Response (200, text/plain)**:
  ```text
  <jwt>
  ```
- **Notes**: Use returned token in `Authorization: Bearer <jwt>`.

---

### 2) Database Interaction (Raw SQL)
Group: `/api/v1/database` — Content type **must** be `application/sql`.

**POST — `/api/v1/database/execute/raw/{name}`**
- **Auth**: Yes (user must have rights to `{name}`)
- **Purpose**: Execute a **non‑query** SQL statement (INSERT/UPDATE/DELETE/DDL).
- **Headers**:
  - `Authorization: Bearer <jwt>`
  - `Content-Type: application/sql`
  - Optional: `timeout: <seconds>` (default ~30)
- **Body**: SQL string (e.g., `CREATE TABLE ...`, `INSERT ...`).
- **Response (200)**: `long` — affected row count (`0` is normal for DDL).

**POST — `/api/v1/database/query/raw/{name}`**
- **Auth**: Yes
- **Purpose**: Execute a **query** (SELECT) and return rows as JSON.
- **Headers**:
  - `Authorization: Bearer <jwt>`
  - `Content-Type: application/sql`
  - Optional: `timeout: <seconds>` (default ~30)
- **Body**: SQL string (e.g., `SELECT * FROM notes`).
- **Response (200, JSON)**:
  ```json
  {
    "items": [ { "id": 1, "body": "hello" }, ... ],
    "totalReturned": <int>,
    "limit": 10000,
    "hitLimit": <true|false>
  }
  ```
- **Notes**: Results are truncated at `limit` (defaults to `10000` rows). `hitLimit=true` indicates more rows existed.

---

### 3) Database Management
Group: `/api/v1/databasemanagement`

**GET — `/api/v1/databasemanagement`**
- **Auth**: `*:admin` or `app:admin`
- **Purpose**: Health/summary (lightweight ping for the management group).

**POST — `/api/v1/databasemanagement/create/{name}`**
- **Auth**: `*:admin` or `app:admin`
- **Purpose**: Create a new database `{name}` (files created on disk).
- **Response**: `200 OK` on success.

**DELETE — `/api/v1/databasemanagement/{name}`**
- **Auth**: `*:admin` or `app:admin`
- **Purpose**: Delete a database `{name}` from disk.

**PUT — `/api/v1/databasemanagement/overwrite/{name}`**
- **Auth**: `*:admin` or `app:admin`
- **Purpose**: Ensure journaling is set correctly. WAL is **always enforced**; this endpoint re‑applies that policy to `{name}`.

**PUT — `/api/v1/databasemanagement/truncate/{name}`**
- **Auth**: `*:admin` or `app:admin`
- **Purpose**: WAL **checkpoint (TRUNCATE)** for `{name}` — flushes WAL into the main DB and truncates the WAL file.

**PUT — `/api/v1/databasemanagement/vacuum/{name}`**
- **Auth**: `*:admin` or `app:admin`
- **Purpose**: Run `VACUUM` on `{name}` to reclaim free space after heavy deletes.

**GET — `/api/v1/databasemanagement/download/{name}`**
- **Auth**: `*:admin` or `app:admin`
- **Purpose**: Download the current `{name}.db` content.

> **WAL policy**: Journal mode **cannot** be disabled. SQLite runs in WAL for safer concurrent access. Use **truncate** and **vacuum** to control file sizes over time.

---

### 4) Subscriptions (Webhooks)
Group: `/api/v1/subscriptions`

**GET — `/api/v1/subscriptions/types`**
- **Auth**: Any authenticated user
- **Purpose**: List available event types (e.g., INSERT/UPDATE/DELETE).

**GET — `/api/v1/subscriptions/{userId}?skip=0&take=10`**
- **Auth**: `*:admin` or `app:admin`
- **Purpose**: Paged list of subscriptions for a specific user.

**GET — `/api/v1/subscriptions/self?skip=0&take=10`**
- **Auth**: Any authenticated user
- **Purpose**: Paged list of the caller’s subscriptions.

**POST — `/api/v1/subscriptions/subscribe`**
- **Auth**: Any authenticated user
- **Purpose**: Create a subscription for a (database, table, event) tuple.
- **Request (JSON)**
  ```json
  {
    "url": "https://example.com/webhooks/litegate",
    "database": "mydb",
    "table": "notes",
    "event": 1,
    "secret": "<base64 random secret>"
  }
  ```

**DELETE — `/api/v1/subscriptions/{recordId}`**
- **Auth**: Owner of the subscription or admin
- **Purpose**: Delete a specific subscription by id.

**DELETE — `/api/v1/subscriptions/clear/{userId}`**
- **Auth**: `*:admin` or `app:admin`
- **Purpose**: Remove all subscriptions for a given user.

**DELETE — `/api/v1/subscriptions/self/clear`**
- **Auth**: Any authenticated user
- **Purpose**: Remove all of **your** subscriptions.

#### Webhook Delivery & HMAC Signing
When a subscribed change occurs, LiteGate sends an HTTP `POST` to your `url` with signed headers and a JSON body describing the change. You can verify the signature using the subscription secret to ensure the payload was not altered in transit.

---

## Operational Notes
- **WAL is always on**: Journal mode is enforced to `WAL` for safer concurrency. You cannot disable it.
- **Space management**: Use `truncate` (checkpoint) and `vacuum` after large write/delete bursts to control file sizes.
- **Timeouts**: You can provide a `timeout` header (seconds) on SQL endpoints.
- **Result limiting**: Query results cap at `limit` (defaults to `10000`).
- **Security**: Store webhook secrets securely. Always validate signatures and timestamps on receive.

---

## Example Workflow
1. Authenticate to get a JWT.
2. Create a DB: `POST /api/v1/databasemanagement/create/mydb`.
3. Create tables / seed data via `POST /api/v1/database/execute/raw/mydb`.
4. Query via `POST /api/v1/database/query/raw/mydb`.
5. Subscribe to changes via `POST /api/v1/subscriptions/subscribe` with a Base64 secret.
6. Verify webhook HMAC on receive.
   

### 🔒 Verifying LiteGate Webhooks

When LiteGate sends a webhook, you should verify its authenticity before acting on the payload. Each request includes:
- `X-Webhook-Timestamp` header (UTC ISO‑8601 string)
- `X-Webhook-Signature` header (format: `t=<timestamp>,v1=<hex-hmac>`)

The signature is an HMAC‑SHA256 hash of the string:

<timestamp>.<raw request body>

using the Base64 secret you provided when creating the subscription.

Here’s an example in **C#**:
```
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

public class LiteGateWebhookVerifier
{
    public static bool Verify(
        string secretBase64,
        string body,
        string timestamp,
        string signatureHeader)
    {
        var parts = signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries);
        string? v1 = null;
        foreach (var part in parts)
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && kv[0].Trim() == "v1")
                v1 = kv[1];
        }
        if (string.IsNullOrEmpty(v1)) return false;

        var canonical = $"{timestamp}.{body}";
        var key = Convert.FromBase64String(secretBase64);

        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(hex),
            Encoding.ASCII.GetBytes(v1));
    }
}

// Example usage inside an ASP.NET controller
[ApiController]
public class WebhooksController : ControllerBase
{
    [HttpPost("/webhooks/litegate")]
    public async Task<IActionResult> HandleWebhook()
    {
        string body = await new StreamReader(Request.Body).ReadToEndAsync();
        string timestamp = Request.Headers["X-Webhook-Timestamp"];
        string signature = Request.Headers["X-Webhook-Signature"];

        string secret = "<your-subscription-secret-base64>";

        if (!LiteGateWebhookVerifier.Verify(secret, body, timestamp, signature))
            return Unauthorized();

        var payload = JsonSerializer.Deserialize<JsonElement>(body);
        Console.WriteLine($"Verified webhook: {payload}");

        return Ok();
    }
}
```

With this setup, only requests signed by LiteGate with your secret will be accepted. This prevents tampering and replay attacks.
```

