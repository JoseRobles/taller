# Charges API

Small payments service. Exposes `POST /charges` (idempotent), `GET /charges/{id}`, and `GET /customers/search?email=`.

## Run

```bash
dotnet run
```

The API listens on `http://localhost:5000` (or whatever ASP.NET defaults to).

## Configuration

The service requires the following configuration in `appsettings.json`:

- **AzureAd** — Microsoft Entra tenant/client IDs for JWT bearer authentication.
- **KeyVault.VaultUri** — URI of the Azure Key Vault instance (e.g. `https://your-vault.vault.azure.net/`).
- **Stripe:ApiKey** — Stored as a secret in Azure Key Vault (secret name: `Stripe--ApiKey`).

## Authentication

All endpoints require a valid Microsoft Entra (Azure AD) JWT bearer token. Include an `Authorization: Bearer <token>` header in every request.

## Quick smoke test

```bash
curl -X POST http://localhost:5000/charges \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <token>" \
  -d '{"idempotencyKey":"k1","amount":12.50,"currency":"USD","customerEmail":"a@b.com","cardToken":"tok_visa"}'
```

## Architecture

The codebase follows a layered architecture:

- **Endpoints** (`Program.cs`) — Thin HTTP layer handling validation and status codes.
- **Service layer** (`ChargeService`) — Business logic: idempotency, payment processing, and audit logging.
- **Store** (`ChargeStore`) — Data access (in-memory, backed by `ConcurrentDictionary`).
- **PaymentProcessor** — External payment gateway integration (Stripe).
- **AuditLog** — Asynchronous SIEM logging.

## Issues fixed

1. **Duplicate charges** — Race condition in the idempotency check. Concurrent requests with the same key could both pass the check before either persisted. Fixed with per-key `SemaphoreSlim` locking.
2. **SQL injection** — Customer search endpoint interpolated user input directly into a SQL string. Fixed with parameterized queries.
3. **Hardcoded Stripe API key** — Secret was embedded as a `const` in source code. Moved to Azure Key Vault, loaded via `IConfiguration` at startup.
