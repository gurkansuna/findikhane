using Findikhane.Api.Contracts;

namespace Findikhane.Api.Data;

/// <summary>
/// data/orders.json içindeki tek bir sipariş kaydının PostgreSQL karşılığı.
/// Not: kredi kartı bilgisi hiçbir zaman burada saklanmaz. T.C. kimlik numarası da
/// bilinçli olarak saklanmıyor (yalnızca iyzico'nun dolandırıcılık kontrolü için anlık
/// olarak kullanılıyor) — admin panelinin sipariş kargolamak için ihtiyaç duyduğu isim,
/// telefon, e-posta, adres ve şehir bilgisi ise (kargo/teslimat amacıyla) burada tutulur.
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

    /// <summary>Alıcının adı soyadı (kargo/teslimat için). Kimlik doğrulama amaçlı değildir.</summary>
    public string BuyerName { get; set; } = "";
    public string BuyerPhone { get; set; } = "";
    public string BuyerEmail { get; set; } = "";
    public string BuyerAddress { get; set; } = "";
    public string BuyerCity { get; set; } = "";
}
