using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderFlow.Api.Data;

namespace OrderFlow.Api.Tests;

public sealed class OrdersApiTests
{
    [Fact]
    public async Task CreateOrder_can_be_read_by_id_and_listed()
    {
        using var factory = CreateFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var request = new
        {
            customerId = "CUST-TEST-01",
            items = new[] { new { productId = "SKU-01", quantity = 3 } }
        };

        var createResponse = await client.PostAsJsonAsync("/api/orders", request);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(created);
        Assert.Equal("CUST-TEST-01", created.CustomerId);
        Assert.Equal(3, Assert.Single(created.Items).Quantity);
        Assert.Equal("Pending", created.Status);

        var getResponse = await client.GetAsync($"/api/orders/{created.Id}");
        var fetched = await getResponse.Content.ReadFromJsonAsync<OrderResponse>();
        var listed = await client.GetFromJsonAsync<OrderResponse[]>("/api/orders");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(created.Id, fetched?.Id);
        Assert.Contains(listed!, order => order.Id == created.Id);
    }

    [Fact]
    public async Task CreateOrder_rejects_missing_customer_and_nonpositive_quantity()
    {
        using var factory = CreateFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        var request = new
        {
            customerId = " ",
            items = new[] { new { productId = "SKU-01", quantity = 0 } }
        };

        var response = await client.PostAsJsonAsync("/api/orders", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Inventory_can_be_set_and_listed()
    {
        using var factory = CreateFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var response = await client.PutAsJsonAsync(
            "/api/inventory/SKU-01",
            new { quantityAvailable = 12 });
        var item = await response.Content.ReadFromJsonAsync<InventoryResponse>();
        var inventory = await client.GetFromJsonAsync<InventoryResponse[]>("/api/inventory");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("SKU-01", item?.ProductId);
        Assert.Equal(12, item?.QuantityAvailable);
        Assert.Contains(inventory!, entry => entry.ProductId == "SKU-01" && entry.QuantityAvailable == 12);
    }

    [Fact]
    public async Task Inventory_rejects_negative_quantity()
    {
        using var factory = CreateFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var response = await client.PutAsJsonAsync(
            "/api/inventory/SKU-01",
            new { quantityAvailable = -1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var databaseName = $"orders-tests-{Guid.NewGuid()}";
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AdminAccount:Email"] = "admin@tests.local",
                    ["AdminAccount:Password"] = "Test-OrderFlow-2026!"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDbContextOptionsConfiguration<OrderDbContext>>();
                services.RemoveAll<DbContextOptions<OrderDbContext>>();
                services.AddDbContext<OrderDbContext>(options =>
                    options.UseInMemoryDatabase(databaseName));
            });
        });
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@tests.local",
            password = "Test-OrderFlow-2026!"
        });
        Assert.Equal(HttpStatusCode.NoContent, loginResponse.StatusCode);
        return client;
    }

    [Fact]
    public async Task Business_routes_require_login_and_logout_clears_the_session()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/inventory")).StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@tests.local",
            password = "Test-OrderFlow-2026!"
        });
        Assert.Equal(HttpStatusCode.NoContent, loginResponse.StatusCode);
        var authCookie = Assert.Single(loginResponse.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", authCookie, StringComparison.OrdinalIgnoreCase);
        var currentUser = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");
        Assert.Equal("admin@tests.local", currentUser?.Email);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/orders")).StatusCode);
    }

    [Fact]
    public async Task Login_rejects_an_incorrect_password()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@tests.local",
            password = "incorrect-password"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record OrderResponse(
        Guid Id,
        string CustomerId,
        OrderItemResponse[] Items,
        string Status);

    private sealed record OrderItemResponse(string ProductId, int Quantity);

    private sealed record InventoryResponse(string ProductId, int QuantityAvailable);

    private sealed record CurrentUserResponse(string Email);
}