using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OrderFlow.Api.Data;
using OrderFlow.Api.Models;
using OrderFlow.InventoryWorker;
using Testcontainers.PostgreSql;

namespace OrderFlow.Api.Tests;

[CollectionDefinition(nameof(PostgreSqlCollection), DisableParallelization = true)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await postgres.DisposeAsync();

    public OrderDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        return new OrderDbContext(options);
    }

    public OrderProcessor CreateProcessor() =>
        new(postgres.GetConnectionString(), NullLogger<OrderProcessor>.Instance);
}

[Collection(nameof(PostgreSqlCollection))]
public sealed class InventoryWorkerTests(PostgreSqlFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var dbContext = fixture.CreateDbContext();
        await dbContext.Orders.ExecuteDeleteAsync();
        await dbContext.InventoryItems.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Processor_confirms_order_and_reserves_all_items()
    {
        var productId = $"SKU-{Guid.NewGuid():N}";
        var orderId = Guid.NewGuid();
        await fixture.SeedAsync(
            [new InventoryItemEntity { ProductId = productId, QuantityAvailable = 5 }],
            CreateOrder(orderId, (productId, 3)));

        var processed = await fixture.CreateProcessor().ProcessNextOrderAsync(CancellationToken.None);

        Assert.True(processed);
        await using var dbContext = fixture.CreateDbContext();
        var order = await dbContext.Orders.SingleAsync(candidate => candidate.Id == orderId);
        var stock = await dbContext.InventoryItems.SingleAsync(item => item.ProductId == productId);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(2, stock.QuantityAvailable);
    }

    private static OrderEntity CreateOrder(Guid orderId, params (string ProductId, int Quantity)[] items) =>
        new()
        {
            Id = orderId,
            CustomerId = $"CUST-{orderId:N}",
            Status = OrderStatus.Pending,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Items = items.Select((item, index) => new OrderItemEntity
            {
                LineNumber = index + 1,
                ProductId = item.ProductId,
                Quantity = item.Quantity
            }).ToList()
        };

    [Fact]
    public async Task Processor_rejects_order_without_partially_reserving_stock()
    {
        var availableProductId = $"SKU-A-{Guid.NewGuid():N}";
        var shortProductId = $"SKU-B-{Guid.NewGuid():N}";
        var orderId = Guid.NewGuid();
        await fixture.SeedAsync(
            [
                new InventoryItemEntity { ProductId = availableProductId, QuantityAvailable = 5 },
                new InventoryItemEntity { ProductId = shortProductId, QuantityAvailable = 1 }
            ],
            CreateOrder(orderId, (availableProductId, 2), (shortProductId, 2)));

        await fixture.CreateProcessor().ProcessNextOrderAsync(CancellationToken.None);

        await using var dbContext = fixture.CreateDbContext();
        var order = await dbContext.Orders.SingleAsync(candidate => candidate.Id == orderId);
        var availableStock = await dbContext.InventoryItems.SingleAsync(item => item.ProductId == availableProductId);
        var shortStock = await dbContext.InventoryItems.SingleAsync(item => item.ProductId == shortProductId);
        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(5, availableStock.QuantityAvailable);
        Assert.Equal(1, shortStock.QuantityAvailable);
    }

    [Fact]
    public async Task Concurrent_processors_do_not_oversell_inventory()
    {
        var productId = $"SKU-{Guid.NewGuid():N}";
        var firstOrderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();
        await fixture.SeedAsync(
            [new InventoryItemEntity { ProductId = productId, QuantityAvailable = 3 }],
            CreateOrder(firstOrderId, (productId, 2)),
            CreateOrder(secondOrderId, (productId, 2)));

        await Task.WhenAll(
            fixture.CreateProcessor().ProcessNextOrderAsync(CancellationToken.None),
            fixture.CreateProcessor().ProcessNextOrderAsync(CancellationToken.None));

        await using var dbContext = fixture.CreateDbContext();
        var statuses = await dbContext.Orders
            .Where(order => order.Id == firstOrderId || order.Id == secondOrderId)
            .Select(order => order.Status)
            .ToArrayAsync();
        var stock = await dbContext.InventoryItems.SingleAsync(item => item.ProductId == productId);
        Assert.Equal(1, statuses.Count(status => status == OrderStatus.Confirmed));
        Assert.Equal(1, statuses.Count(status => status == OrderStatus.Rejected));
        Assert.Equal(1, stock.QuantityAvailable);
    }

}

public static class PostgreSqlFixtureExtensions
{
    public static async Task SeedAsync(
        this PostgreSqlFixture fixture,
        InventoryItemEntity[] inventory,
        params OrderEntity[] orders)
    {
        await using var dbContext = fixture.CreateDbContext();
        await dbContext.InventoryItems.AddRangeAsync(inventory);
        await dbContext.Orders.AddRangeAsync(orders);
        await dbContext.SaveChangesAsync();
    }
}
