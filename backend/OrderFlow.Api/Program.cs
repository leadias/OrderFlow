using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Api.Data;
using OrderFlow.Api.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("OrdersDb")));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    if (dbContext.Database.IsNpgsql())
    {
        await dbContext.Database.MigrateAsync();
    }
    else
    {
        await dbContext.Database.EnsureCreatedAsync();
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health", async (OrderDbContext dbContext, CancellationToken cancellationToken) =>
    await dbContext.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "healthy" })
        : Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Database unavailable"));

var orders = app.MapGroup("/api/orders");

orders.MapPost("", async (CreateOrderRequest request, OrderDbContext dbContext, CancellationToken cancellationToken) =>
{
    var errors = Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var order = new OrderEntity
    {
        Id = Guid.NewGuid(),
        CustomerId = request.CustomerId!.Trim(),
        Status = OrderStatus.Pending,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        Items = request.Items!.Select((item, index) => new OrderItemEntity
        {
            LineNumber = index + 1,
            ProductId = item.ProductId!.Trim(),
            Quantity = item.Quantity
        }).ToList()
    };

    dbContext.Orders.Add(order);
    await dbContext.SaveChangesAsync(cancellationToken);

    var response = ToResponse(order);
    return Results.Created($"/api/orders/{order.Id}", response);
})
.WithName("CreateOrder");

orders.MapGet("", async (OrderDbContext dbContext, CancellationToken cancellationToken) =>
{
    var orders = await dbContext.Orders
        .AsNoTracking()
        .Include(order => order.Items)
        .OrderByDescending(order => order.CreatedAtUtc)
        .ToListAsync(cancellationToken);

    return Results.Ok(orders.Select(ToResponse));
})
    .WithName("GetOrders");

orders.MapGet("/{id:guid}", async (Guid id, OrderDbContext dbContext, CancellationToken cancellationToken) =>
{
    var order = await dbContext.Orders
        .AsNoTracking()
        .Include(candidate => candidate.Items)
        .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

    return order is null ? Results.NotFound() : Results.Ok(ToResponse(order));
})
.WithName("GetOrder");

var inventory = app.MapGroup("/api/inventory");

inventory.MapGet("", async (OrderDbContext dbContext, CancellationToken cancellationToken) =>
{
    var items = await dbContext.InventoryItems
        .AsNoTracking()
        .OrderBy(item => item.ProductId)
        .Select(item => new InventoryItemResponse(item.ProductId, item.QuantityAvailable))
        .ToListAsync(cancellationToken);

    return Results.Ok(items);
})
.WithName("GetInventory");

inventory.MapPut("/{productId}", async (
    string productId,
    SetInventoryRequest request,
    OrderDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var normalizedProductId = productId.Trim();
    if (string.IsNullOrWhiteSpace(normalizedProductId) || normalizedProductId.Length > 100)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["productId"] = ["ProductId must contain between 1 and 100 characters."]
        });
    }

    if (request.QuantityAvailable < 0)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["quantityAvailable"] = ["QuantityAvailable cannot be negative."]
        });
    }

    var item = await dbContext.InventoryItems.FindAsync([normalizedProductId], cancellationToken);
    if (item is null)
    {
        item = new InventoryItemEntity { ProductId = normalizedProductId };
        dbContext.InventoryItems.Add(item);
    }

    item.QuantityAvailable = request.QuantityAvailable;
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new InventoryItemResponse(item.ProductId, item.QuantityAvailable));
})
.WithName("SetInventory");

app.Run();

static Dictionary<string, string[]> Validate(CreateOrderRequest request)
{
    var errors = new Dictionary<string, string[]>();

    if (string.IsNullOrWhiteSpace(request.CustomerId))
    {
        errors["customerId"] = ["CustomerId is required."];
    }

    if (request.Items is null || request.Items.Count == 0)
    {
        errors["items"] = ["At least one order item is required."];
        return errors;
    }

    for (var index = 0; index < request.Items.Count; index++)
    {
        var item = request.Items[index];
        if (string.IsNullOrWhiteSpace(item.ProductId))
        {
            errors[$"items[{index}].productId"] = ["ProductId is required."];
        }

        if (item.Quantity <= 0)
        {
            errors[$"items[{index}].quantity"] = ["Quantity must be greater than zero."];
        }
    }

    return errors;
}

static OrderResponse ToResponse(OrderEntity order) =>
    new(
        order.Id,
        order.CustomerId,
        order.Items.OrderBy(item => item.LineNumber)
            .Select(item => new OrderItemResponse(item.ProductId, item.Quantity))
            .ToArray(),
        order.Status,
        order.CreatedAtUtc);

public partial class Program;
