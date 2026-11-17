using Npgsql;

namespace OrderFlow.InventoryWorker;

public sealed class OrderProcessor(string connectionString, ILogger<OrderProcessor> logger)
{
    public async Task<bool> ProcessNextOrderAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        Guid orderId;
        await using (var command = new NpgsqlCommand(
            """
            SELECT "Id"
            FROM orders
            WHERE "Status" = 'Pending'
            ORDER BY "CreatedAtUtc", "Id"
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """,
            connection,
            transaction))
        {
            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            orderId = (Guid)result;
        }

        var requiredQuantities = new SortedDictionary<string, int>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand(
            """
            SELECT "ProductId", SUM("Quantity")::integer
            FROM order_items
            WHERE "OrderId" = @orderId
            GROUP BY "ProductId"
            ORDER BY "ProductId"
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("orderId", orderId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                requiredQuantities.Add(reader.GetString(0), reader.GetInt32(1));
            }
        }

        var availableQuantities = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var productId in requiredQuantities.Keys)
        {
            await using var command = new NpgsqlCommand(
                "SELECT \"QuantityAvailable\" FROM inventory_items WHERE \"ProductId\" = @productId FOR UPDATE",
                connection,
                transaction);
            command.Parameters.AddWithValue("productId", productId);
            var available = await command.ExecuteScalarAsync(cancellationToken);
            if (available is not null)
            {
                availableQuantities.Add(productId, (int)available);
            }
        }

        var unavailableProduct = requiredQuantities.FirstOrDefault(required =>
            !availableQuantities.TryGetValue(required.Key, out var available) || available < required.Value);

        if (!string.IsNullOrEmpty(unavailableProduct.Key))
        {
            var available = availableQuantities.GetValueOrDefault(unavailableProduct.Key);
            logger.LogWarning(
                "Rejected order {OrderId}: product {ProductId} requires {RequiredQuantity}, available {AvailableQuantity}.",
                orderId,
                unavailableProduct.Key,
                unavailableProduct.Value,
                available);
            await UpdateOrderStatusAsync(connection, transaction, orderId, "Rejected", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        foreach (var required in requiredQuantities)
        {
            await using var command = new NpgsqlCommand(
                "UPDATE inventory_items SET \"QuantityAvailable\" = \"QuantityAvailable\" - @quantity WHERE \"ProductId\" = @productId",
                connection,
                transaction);
            command.Parameters.AddWithValue("quantity", required.Value);
            command.Parameters.AddWithValue("productId", required.Key);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await UpdateOrderStatusAsync(connection, transaction, orderId, "Confirmed", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Confirmed order {OrderId} and reserved inventory.", orderId);
        return true;
    }

    private static async Task UpdateOrderStatusAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid orderId,
        string status,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE orders SET \"Status\" = @status WHERE \"Id\" = @orderId",
            connection,
            transaction);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("orderId", orderId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
