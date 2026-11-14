using System.Collections.Concurrent;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<OrderStore>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

var orders = app.MapGroup("/api/orders");

orders.MapPost("", (CreateOrderRequest request, OrderStore store) =>
{
    var errors = Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var order = store.Create(request);
    return Results.Created($"/api/orders/{order.Id}", order);
})
.WithName("CreateOrder");

orders.MapGet("", (OrderStore store) => Results.Ok(store.GetAll()))
    .WithName("GetOrders");

orders.MapGet("/{id:guid}", (Guid id, OrderStore store) =>
{
    var order = store.Get(id);
    return order is null ? Results.NotFound() : Results.Ok(order);
})
.WithName("GetOrder");

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

sealed record CreateOrderRequest(string CustomerId, List<CreateOrderItemRequest>? Items);

sealed record CreateOrderItemRequest(string ProductId, int Quantity);

sealed record OrderItem(string ProductId, int Quantity);

sealed record Order(
    Guid Id,
    string CustomerId,
    IReadOnlyList<OrderItem> Items,
    OrderStatus Status,
    DateTimeOffset CreatedAtUtc);

enum OrderStatus
{
    Pending
}

sealed class OrderStore
{
    private readonly ConcurrentDictionary<Guid, Order> orders = new();

    public Order Create(CreateOrderRequest request)
    {
        var order = new Order(
            Guid.NewGuid(),
            request.CustomerId.Trim(),
            request.Items!.Select(item => new OrderItem(item.ProductId.Trim(), item.Quantity)).ToArray(),
            OrderStatus.Pending,
            DateTimeOffset.UtcNow);

        orders[order.Id] = order;
        return order;
    }

    public IReadOnlyCollection<Order> GetAll() =>
        orders.Values.OrderByDescending(order => order.CreatedAtUtc).ToArray();

    public Order? Get(Guid id) => orders.GetValueOrDefault(id);
}
