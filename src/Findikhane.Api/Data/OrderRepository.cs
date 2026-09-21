using System.Text.Json;
using Findikhane.Api.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Findikhane.Api.Data;

/// <summary>
/// readOrders / saveOrders (JSON dosyası) fonksiyonlarının yerini alan, PostgreSQL
/// tabanlı sipariş deposu. Eşzamanlı yazımlar artık dosya yeniden adlandırma yerine
/// veritabanı işlemleriyle (ve token için tekil indeksle) güvenceye alınıyor.
/// </summary>
public sealed class OrderRepository
{
    private const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS orders (
            order_id        text PRIMARY KEY,
            created_at      timestamptz NOT NULL,
            completed_at    timestamptz NULL,
            cart            jsonb NOT NULL,
            conversation_id text NOT NULL,
            total           numeric(12,2) NOT NULL,
            payment_status  text NOT NULL,
            token           text NULL,
            payment_id      text NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS idx_orders_token ON orders (token) WHERE token IS NOT NULL;
        """;

    private static readonly JsonSerializerOptions CartJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly NpgsqlDataSource _dataSource;

    public OrderRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(CreateTableSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertAsync(OrderRecord order, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO orders (order_id, created_at, cart, conversation_id, total, payment_status, token)
            VALUES (@orderId, @createdAt, @cart, @conversationId, @total, @paymentStatus, @token)
            """, connection);

        command.Parameters.AddWithValue("orderId", order.OrderId);
        command.Parameters.AddWithValue("createdAt", order.CreatedAt);
        command.Parameters.Add(new NpgsqlParameter("cart", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(order.Cart, CartJsonOptions) });
        command.Parameters.AddWithValue("conversationId", order.ConversationId);
        command.Parameters.AddWithValue("total", order.Total);
        command.Parameters.AddWithValue("paymentStatus", order.PaymentStatus);
        command.Parameters.AddWithValue("token", order.Token is null ? (object)DBNull.Value : order.Token);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetTokenAsync(string orderId, string token, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("UPDATE orders SET token = @token WHERE order_id = @orderId", connection);
        command.Parameters.AddWithValue("token", token);
        command.Parameters.AddWithValue("orderId", orderId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<OrderRecord?> FindByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT order_id, created_at, completed_at, cart, conversation_id, total, payment_status, token, payment_id
            FROM orders WHERE token = @token LIMIT 1
            """, connection);
        command.Parameters.AddWithValue("token", token);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return Map(reader);
    }

    public async Task CompletePaymentAsync(string orderId, bool completed, string? paymentId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE orders
            SET payment_status = @paymentStatus, payment_id = @paymentId, completed_at = @completedAt
            WHERE order_id = @orderId
            """, connection);
        command.Parameters.AddWithValue("paymentStatus", completed ? "SUCCESS" : "FAILURE");
        command.Parameters.AddWithValue("paymentId", paymentId is null ? (object)DBNull.Value : paymentId);
        command.Parameters.AddWithValue("completedAt", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("orderId", orderId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static OrderRecord Map(NpgsqlDataReader reader)
    {
        var cartJson = reader.GetString(reader.GetOrdinal("cart"));
        var cart = JsonSerializer.Deserialize<List<CartLine>>(cartJson, CartJsonOptions) ?? new List<CartLine>();

        return new OrderRecord
        {
            OrderId = reader.GetString(reader.GetOrdinal("order_id")),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            CompletedAt = reader.IsDBNull(reader.GetOrdinal("completed_at")) ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("completed_at")),
            Cart = cart,
            ConversationId = reader.GetString(reader.GetOrdinal("conversation_id")),
            Total = reader.GetDecimal(reader.GetOrdinal("total")),
            PaymentStatus = reader.GetString(reader.GetOrdinal("payment_status")),
            Token = reader.IsDBNull(reader.GetOrdinal("token")) ? null : reader.GetString(reader.GetOrdinal("token")),
            PaymentId = reader.IsDBNull(reader.GetOrdinal("payment_id")) ? null : reader.GetString(reader.GetOrdinal("payment_id"))
        };
    }
}
