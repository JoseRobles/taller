# Charges API

Small payments service. Exposes `POST /charges` (idempotent), `GET /charges/{id}`, and `GET /customers/search?email=`.

## Run

```bash
dotnet run
```

The API listens on `http://localhost:5000` (or whatever ASP.NET defaults to).

## Quick smoke test

```bash
curl -X POST http://localhost:5000/charges \
  -H "Content-Type: application/json" \
  -d '{"idempotencyKey":"k1","amount":12.50,"currency":"USD","customerEmail":"a@b.com","cardToken":"tok_visa"}'
```

## What's known broken

Customers are reporting two issues:
1. Some are seeing duplicate charges for the same purchase.
2. Our security team flagged something on this endpoint in their last review.

There may be more. Find what you can.
