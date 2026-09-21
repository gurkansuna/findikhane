using Findikhane.Api.Contracts;

namespace Findikhane.Api.Data;

/// <summary>
/// data/orders.json içindeki tek bir sipariş kaydının PostgreSQL karşılığı.
/// Not: orijinal koddaki gibi kredi kartı veya kimlik bilgisi burada saklanmaz;
/// sadece sepet içeriği, tutar ve iyzico ile konuşmak için gereken alanlar tutulur.
/// </summary>
public sealed class OrderRecord
{
    public required string OrderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public required List<CartLine> Cart { get; set; }
    public required string ConversationId { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = "PENDING";
    public string? Token { get; set; }
    public string? PaymentId { get; set; }
}
