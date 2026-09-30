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

    // Admin paneli (kargo/teslimat) için sonradan eklenen alıcı bilgisi sütunları.
    // ALTER ... ADD COLUMN IF NOT EXISTS kullanılıyor ki tablo daha önce (bu alanlar
    // olmadan) oluşturulmuş gerçek bir veritabanında da yeniden başlatınca sorunsuz
    // geriye dönük "migrate" edilsin — elle bir migration çalıştırmaya gerek kalmaz.
    private static readonly string[] MigrationStatements =
    [
        "ALTER TABLE orders ADD COLUMN IF NOT EXISTS buyer_name text NOT NULL DEFAULT ''",
        "ALTER TABLE orders ADD COLUMN IF NOT EXISTS buyer_phone text NOT NULL DEFAULT ''",
        "ALTER TABLE orders ADD COLUMN IF NOT EXISTS buyer_email text NOT NULL DEFAULT ''",
        "ALTER TABLE orders ADD COLUMN IF NOT EXISTS buyer_address text NOT NULL DEFAULT ''",
        "ALTER TABLE orders ADD COLUMN IF NOT EXISTS buyer_city text NOT NULL DEFAULT ''",
        "CREATE INDEX IF NOT EXISTS idx_orders_created_at ON orders (created_at DESC)"
    ];

    private static readonly JsonSerializerOptions CartJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly NpgsqlDataSource _dataSource;

    public OrderRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(CreateTableSql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var statement in MigrationStatements)
        {
            await using var command = new NpgsqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task InsertAsync(OrderRecord order, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO orders (order_id, created_at, cart, conversation_id, total, payment_status, token,
                                 buyer_name, buyer_phone, buyer_email, buyer_address, buyer_city)
            VALUES (@orderId, @createdAt, @cart, @conversationId, @total, @paymentStatus, @token,
                    @buyerName, @buyerPhone, @buyerEmail, @buyerAddress, @buyerCity)
            """, connection);

        command.Parameters.AddWithValue("orderId", order.OrderId);
        command.Parameters.AddWithValue("createdAt", order.CreatedAt);
        command.Parameters.Add(new NpgsqlParameter("cart", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(order.Cart, CartJsonOptions) });
        command.Parameters.AddWithValue("conversationId", order.ConversationId);
        command.Parameters.AddWithValue("total", order.Total);
        command.Parameters.AddWithValue("paymentStatus", order.PaymentStatus);
        command.Parameters.AddWithValue("token", order.Token is null ? (object)DBNull.Value : order.Token);
        command.Parameters.AddWithValue("buyerName", order.BuyerName);
        command.Parameters.AddWithValue("buyerPhone", order.BuyerPhone);
        command.Parameters.AddWithValue("buyerEmail", order.BuyerEmail);
        command.Parameters.AddWithValue("buyerAddress", order.BuyerAddress);
        command.Parameters.AddWithValue("buyerCity", order.BuyerCity);

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
            PaymentId = reader.IsDBNull(reader.GetOrdinal("payment_id")) ? null : reader.GetString(reader.GetOrdinal("payment_id")),
            BuyerName = reader.GetString(reader.GetOrdinal("buyer_name")),
            BuyerPhone = reader.GetString(reader.GetOrdinal("buyer_phone")),
            BuyerEmail = reader.GetString(reader.GetOrdinal("buyer_email")),
            BuyerAddress = reader.GetString(reader.GetOrdinal("buyer_address")),
            BuyerCity = reader.GetString(reader.GetOrdinal("buyer_city"))
        };
    }

    /// <summary>
    /// Admin paneli (/admin) için sayfalanmış, isteğe bağlı durum/arama filtreli sipariş
    /// listesi. En yeni sipariş en üstte (created_at DESC). Arama; sipariş no, alıcı adı,
    /// telefon ve e-posta üzerinde (case-insensitive) basit bir ILIKE eşleşmesidir.
    /// </summary>
    public async Task<(IReadOnlyList<OrderRecord> Orders, int TotalCount)> GetOrdersAsync(
        string? statusFilter, string? searchText, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var whereClauses = new List<string>();
        // (parametreAdı, değer) çiftleri: NpgsqlParameter'ı elle yeniden kurmak yerine her
        // komuta AddWithValue ile eklenir — projede zaten kullanılan tek imza bu (bkz.
        // InsertAsync/CompletePaymentAsync), gerçek Npgsql ile derleme zamanı uyumsuzluğu riski yok.
        var parameters = new List<(string Name, object Value)>();

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            whereClauses.Add("payment_status = @status");
            parameters.Add(("status", statusFilter));
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            whereClauses.Add("(order_id ILIKE @search OR buyer_name ILIKE @search OR buyer_phone ILIKE @search OR buyer_email ILIKE @search)");
            parameters.Add(("search", $"%{searchText}%"));
        }

        var whereSql = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        int totalCount;
        await using (var countCommand = new NpgsqlCommand($"SELECT COUNT(*) FROM orders {whereSql}", connection))
        {
            foreach (var (name, value) in parameters) countCommand.Parameters.AddWithValue(name, value);
            totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
        }

        var orders = new List<OrderRecord>();
        await using (var listCommand = new NpgsqlCommand(
            $"""
            SELECT order_id, created_at, completed_at, cart, conversation_id, total, payment_status, token, payment_id,
                   buyer_name, buyer_phone, buyer_email, buyer_address, buyer_city
            FROM orders {whereSql}
            ORDER BY created_at DESC
            LIMIT @limit OFFSET @offset
            """, connection))
        {
            foreach (var (name, value) in parameters) listCommand.Parameters.AddWithValue(name, value);
            listCommand.Parameters.AddWithValue("limit", pageSize);
            listCommand.Parameters.AddWithValue("offset", (page - 1) * pageSize);

            await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                orders.Add(Map(reader));
            }
        }

        return (orders, totalCount);
    }
}
