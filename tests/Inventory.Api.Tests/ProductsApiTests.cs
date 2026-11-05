using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Inventory.Api.Tests;

[TestClass]
public class ProductsApiTests
{
    private ApiHost _api = null!;
    private HttpClient Client => _api.Client;

    [TestInitialize]
    public async Task Start() => _api = await ApiHost.StartAsync();

    [TestCleanup]
    public async Task Stop() => await _api.DisposeAsync();

    private async Task<Product> CreateProductAsync(string sku = "ABC-1", int stock = 10)
    {
        var response = await Client.PostAsJsonAsync("/products", new { sku, name = "Toner", price = 19.99m, stock });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Product>())!;
    }

    [TestMethod]
    [TestCategory("Smoke")]
    public async Task Health_ReturnsOk()
    {
        var response = await Client.GetAsync("/health");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    [TestCategory("Smoke")]
    public async Task Create_ReturnsCreatedWithLocationThatResolves()
    {
        var response = await Client.PostAsJsonAsync("/products", new { sku = " abc-1 ", name = "Toner", price = 19.99m, stock = 5 });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<Product>();
        Assert.AreEqual("ABC-1", created!.Sku, "SKU should be trimmed and upper-cased.");

        var fetched = await Client.GetFromJsonAsync<Product>(response.Headers.Location);
        Assert.AreEqual(created, fetched);
    }

    [TestMethod]
    [TestCategory("Contract")]
    public async Task Product_JsonContract_IsStable()
    {
        var product = await CreateProductAsync();
        using var json = JsonDocument.Parse(await Client.GetStringAsync($"/products/{product.Id}"));

        var properties = json.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind);

        CollectionAssert.AreEquivalent(new[] { "id", "sku", "name", "price", "stock" }, properties.Keys.ToArray());
        Assert.AreEqual(JsonValueKind.String, properties["id"]);
        Assert.AreEqual(JsonValueKind.String, properties["sku"]);
        Assert.AreEqual(JsonValueKind.Number, properties["price"]);
        Assert.AreEqual(JsonValueKind.Number, properties["stock"]);
    }

    [TestMethod]
    [TestCategory("Negative")]
    [DataRow("", "Toner", 10.0, 1, "sku", DisplayName = "empty SKU")]
    [DataRow("ABC", "   ", 10.0, 1, "name", DisplayName = "blank name")]
    [DataRow("ABC", "Toner", 0.0, 1, "price", DisplayName = "zero price")]
    [DataRow("ABC", "Toner", -1.0, 1, "price", DisplayName = "negative price")]
    [DataRow("ABC", "Toner", 10.0, -1, "stock", DisplayName = "negative stock")]
    public async Task Create_InvalidInput_ReturnsFieldError(string sku, string name, double price, int stock, string field)
    {
        var response = await Client.PostAsJsonAsync("/products", new { sku, name, price, stock });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(problem.RootElement.GetProperty("errors").TryGetProperty(field, out _), $"Expected an error for '{field}'.");
    }

    [TestMethod]
    [TestCategory("Negative")]
    public async Task Create_DuplicateSku_ReturnsConflict()
    {
        await CreateProductAsync("DUP-1");
        var response = await Client.PostAsJsonAsync("/products", new { sku = "dup-1", name = "Other", price = 1m, stock = 1 });
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    [TestCategory("Negative")]
    public async Task Get_UnknownId_ReturnsNotFound()
    {
        var response = await Client.GetAsync($"/products/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Reserve_ReducesStock()
    {
        var product = await CreateProductAsync(stock: 10);
        var response = await Client.PostAsJsonAsync($"/products/{product.Id}/reserve", new { quantity = 4 });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(6, (await response.Content.ReadFromJsonAsync<Product>())!.Stock);
    }

    [TestMethod]
    [DataRow(10, 10, HttpStatusCode.OK, DisplayName = "exactly all stock")]
    [DataRow(10, 11, HttpStatusCode.Conflict, DisplayName = "one more than stock")]
    [DataRow(10, 0, HttpStatusCode.BadRequest, DisplayName = "zero quantity")]
    [DataRow(10, -1, HttpStatusCode.BadRequest, DisplayName = "negative quantity")]
    public async Task Reserve_BoundaryValues(int stock, int quantity, HttpStatusCode expected)
    {
        var product = await CreateProductAsync(stock: stock);
        var response = await Client.PostAsJsonAsync($"/products/{product.Id}/reserve", new { quantity });
        Assert.AreEqual(expected, response.StatusCode);
    }

    [TestMethod]
    [TestCategory("Concurrency")]
    public async Task Reserve_ConcurrentRequests_NeverOversell()
    {
        var product = await CreateProductAsync(stock: 10);

        var responses = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ =>
            Client.PostAsJsonAsync($"/products/{product.Id}/reserve", new { quantity = 1 })));

        Assert.AreEqual(10, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.AreEqual(40, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var final = await Client.GetFromJsonAsync<Product>($"/products/{product.Id}");
        Assert.AreEqual(0, final!.Stock);
    }

    [TestMethod]
    public async Task Delete_RemovesProduct_AndSecondDeleteIsNotFound()
    {
        var product = await CreateProductAsync();
        Assert.AreEqual(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/products/{product.Id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Client.DeleteAsync($"/products/{product.Id}")).StatusCode);
    }
}
