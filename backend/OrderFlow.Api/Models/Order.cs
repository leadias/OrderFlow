namespace OrderFlow.Api.Models;

public enum OrderStatus
{
    Pending,
    Confirmed,
    Rejected
}

public sealed class OrderEntity
{
    public Guid Id { get; set; }
    public required string CustomerId { get; set; }
    public OrderStatus Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public List<OrderItemEntity> Items { get; set; } = [];
}

public sealed class OrderItemEntity
{
    public Guid OrderId { get; set; }
    public int LineNumber { get; set; }
    public required string ProductId { get; set; }
    public int Quantity { get; set; }
    public OrderEntity Order { get; set; } = null!;
}

public sealed class InventoryItemEntity
{
    public required string ProductId { get; set; }
    public int QuantityAvailable { get; set; }
}

public sealed record CreateOrderRequest(string? CustomerId, List<CreateOrderItemRequest>? Items);

public sealed record CreateOrderItemRequest(string? ProductId, int Quantity);

public sealed record LoginRequest(string Email, string Password);

public sealed record SetInventoryRequest(int QuantityAvailable);

public sealed record InventoryItemResponse(string ProductId, int QuantityAvailable);

public sealed record OrderItemResponse(string ProductId, int Quantity);

public sealed record OrderResponse(
    Guid Id,
    string CustomerId,
    IReadOnlyList<OrderItemResponse> Items,
    OrderStatus Status,
    DateTimeOffset CreatedAtUtc);