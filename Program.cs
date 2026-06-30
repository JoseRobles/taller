using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Microsoft.Identity.Web;


var builder = WebApplication.CreateBuilder(args);

// Azure Key Vault configuration provider
var keyVaultUrl = builder.Configuration["KeyVault:VaultUri"];
if (!string.IsNullOrEmpty(keyVaultUrl))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUrl),
        new Azure.Identity.DefaultAzureCredential());
}

// Entra (Azure AD) authentication
builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

builder.Services.AddSingleton<ChargeStore>();
builder.Services.AddSingleton<PaymentProcessor>();
builder.Services.AddSingleton<AuditLog>();
builder.Services.AddSingleton<ChargeService>();
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// ============================================================
//  Charges API — small payments service
// ============================================================

app.MapPost("/charges", async (ChargeRequest req, ChargeService service) =>
{
    if (string.IsNullOrWhiteSpace(req.IdempotencyKey))
        return Results.BadRequest(new { error = "idempotencyKey is required" });

    var result = await service.CreateChargeAsync(req);
    return Results.Created($"/charges/{result.Charge.Id}", result.Charge);
}).RequireAuthorization();

app.MapGet("/charges/{id}", (string id, ChargeService service) =>
{
    var charge = service.GetChargeById(id);
    return charge is null ? Results.NotFound() : Results.Ok(charge);
}).RequireAuthorization();

app.MapGet("/customers/search", (string email, ChargeService service) =>
{
    var results = service.FindChargesByEmail(email);
    return Results.Ok(results);
}).RequireAuthorization();

app.Run();

// Expose Program to the test project (WebApplicationFactory<Program>)
public partial class Program { }

// ============================================================
//  Domain
// ============================================================

public record ChargeRequest(
    [property: JsonPropertyName("idempotencyKey")] string IdempotencyKey,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("customerEmail")] string CustomerEmail,
    [property: JsonPropertyName("cardToken")] string CardToken
);

public record Charge(
    string Id,
    decimal Amount,
    string Currency,
    string CustomerEmail,
    string Status,
    DateTime CreatedAt
);

// ============================================================
//  Service layer
// ============================================================

public record CreateChargeResult(Charge Charge, bool WasExisting);

public class ChargeService
{
    private readonly ChargeStore _store;
    private readonly PaymentProcessor _processor;
    private readonly AuditLog _audit;

    public ChargeService(ChargeStore store, PaymentProcessor processor, AuditLog audit)
    {
        _store = store;
        _processor = processor;
        _audit = audit;
    }

    public async Task<CreateChargeResult> CreateChargeAsync(ChargeRequest req)
    {
        var semaphore = _store.GetLock(req.IdempotencyKey);
        await semaphore.WaitAsync();
        try
        {
            // Idempotency check (safe under per-key lock)
            if (_store.TryGet(req.IdempotencyKey, out var existing))
            {
                return new CreateChargeResult(existing!, WasExisting: true);
            }

            // Process the charge
            var charge = await _processor.ChargeAsync(req);

            // Persist
            _store.Save(req.IdempotencyKey, charge);

            // Audit (don't block — log asynchronously)
            _ = _audit.LogChargeAsync(charge, req.CustomerEmail);

            return new CreateChargeResult(charge, WasExisting: false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public Charge? GetChargeById(string id) => _store.GetById(id);

    public List<Charge> FindChargesByEmail(string email) => _store.FindByEmail(email);
}

// ============================================================
//  Store
// ============================================================

public class ChargeStore
{
    private readonly ConcurrentDictionary<string, Charge> _byKey = new();
    private readonly ConcurrentDictionary<string, Charge> _byId = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly List<Charge> _all = new();

    public SemaphoreSlim GetLock(string key) =>
        _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

    public bool TryGet(string key, out Charge? charge)
    {
        return _byKey.TryGetValue(key, out charge);
    }

    public void Save(string key, Charge charge)
    {
        _byKey[key] = charge;
        _byId[charge.Id] = charge;
        _all.Add(charge);
    }

    public Charge? GetById(string id) => _byId.TryGetValue(id, out var c) ? c : null;

    public List<Charge> FindByEmail(string email)
    {
        // Use parameterized query to prevent SQL injection
        var query = "SELECT * FROM charges WHERE customer_email = @email";
        Console.WriteLine($"[ChargeStore] running: {query} (@email = {email})");

        // For this in-memory demo we just filter in-process,
        // but the same parameterized query goes to the SQL adapter in production.
        return _all.Where(c => c.CustomerEmail == email).ToList();
    }
}

// ============================================================
//  Payment processor (calls a fake external service)
// ============================================================

public class PaymentProcessor
{
    private readonly string _stripeApiKey;

    public PaymentProcessor(IConfiguration configuration)
    {
        _stripeApiKey = configuration["Stripe:ApiKey"]
            ?? throw new InvalidOperationException("Stripe:ApiKey is not configured. Add it to Azure Key Vault.");
    }

    public async Task<Charge> ChargeAsync(ChargeRequest req)
    {
        // Simulate latency talking to the processor
        await Task.Delay(250);

        var id = "ch_" + Guid.NewGuid().ToString("N")[..16];
        return new Charge(
            Id: id,
            Amount: req.Amount,
            Currency: req.Currency,
            CustomerEmail: req.CustomerEmail,
            Status: "succeeded",
            CreatedAt: DateTime.UtcNow
        );
    }
}

// ============================================================
//  Audit log
// ============================================================

public class AuditLog
{
    public async Task LogChargeAsync(Charge charge, string customerEmail)
    {
        // Pretend we write to a SIEM
        await Task.Delay(50);
        Console.WriteLine($"[audit] charge={charge.Id} amount={charge.Amount} {charge.Currency} email={customerEmail} cardToken=*** at={charge.CreatedAt:O}");
    }
}
