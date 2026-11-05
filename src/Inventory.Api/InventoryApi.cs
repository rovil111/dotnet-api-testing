using System.Collections.Concurrent;

namespace Inventory.Api;

public record Product(Guid Id, string Sku, string Name, decimal Price, int Stock);
public record CreateProduct(string? Sku, string? Name, decimal Price, int Stock);
public record StockChange(int Quantity);

public class ProductStore
{
    private readonly ConcurrentDictionary<Guid, Product> _products = new();
    private readonly object _writeLock = new();

    public IEnumerable<Product> All() => _products.Values.OrderBy(p => p.Sku);
    public Product? Find(Guid id) => _products.GetValueOrDefault(id);
    public bool Remove(Guid id) => _products.TryRemove(id, out _);

    public Product? Add(CreateProduct input)
    {
        lock (_writeLock)
        {
            if (_products.Values.Any(p => p.Sku.Equals(input.Sku, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }
            var product = new Product(Guid.NewGuid(), input.Sku!.Trim().ToUpperInvariant(), input.Name!.Trim(), input.Price, input.Stock);
            _products[product.Id] = product;
            return product;
        }
    }

    /// <summary>Reserves stock atomically. Returns null when too little stock is left.</summary>
    public Product? Reserve(Guid id, int quantity)
    {
        lock (_writeLock)
        {
            var current = _products[id];
            if (current.Stock < quantity)
            {
                return null;
            }
            var updated = current with { Stock = current.Stock - quantity };
            _products[id] = updated;
            return updated;
        }
    }
}

public static class InventoryApi
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton<ProductStore>();
        var app = builder.Build();

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/products", (ProductStore store) => store.All());

        app.MapGet("/products/{id:guid}", (Guid id, ProductStore store) =>
            store.Find(id) is { } product ? Results.Ok(product) : Results.NotFound());

        app.MapPost("/products", (CreateProduct input, ProductStore store) =>
        {
            var errors = Validate(input);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }
            var created = store.Add(input);
            return created is null
                ? Results.Conflict(new { error = $"SKU '{input.Sku}' already exists." })
                : Results.Created($"/products/{created.Id}", created);
        });

        app.MapPost("/products/{id:guid}/reserve", (Guid id, StockChange change, ProductStore store) =>
        {
            if (change.Quantity <= 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["quantity"] = ["Quantity must be greater than zero."],
                });
            }
            if (store.Find(id) is null)
            {
                return Results.NotFound();
            }
            var updated = store.Reserve(id, change.Quantity);
            return updated is null
                ? Results.Conflict(new { error = "Not enough stock." })
                : Results.Ok(updated);
        });

        app.MapDelete("/products/{id:guid}", (Guid id, ProductStore store) =>
            store.Remove(id) ? Results.NoContent() : Results.NotFound());

        return app;
    }

    private static Dictionary<string, string[]> Validate(CreateProduct input)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.Sku))
        {
            errors["sku"] = ["SKU is required."];
        }
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            errors["name"] = ["Name is required."];
        }
        if (input.Price <= 0)
        {
            errors["price"] = ["Price must be greater than zero."];
        }
        if (input.Stock < 0)
        {
            errors["stock"] = ["Stock cannot be negative."];
        }
        return errors;
    }
}
