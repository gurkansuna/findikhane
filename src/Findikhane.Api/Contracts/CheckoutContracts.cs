using System.Text.Json;
using System.Text.Json.Serialization;

namespace Findikhane.Api.Contracts;

/// <summary>POST /api/checkout gövdesinin ham (doğrulanmamış) şekli.</summary>
public sealed class CheckoutRequestDto
{
    [JsonPropertyName("buyer")]
    public RawBuyerDto? Buyer { get; set; }

    [JsonPropertyName("items")]
    public List<RawCartItemDto>? Items { get; set; }
}

public sealed class RawBuyerDto
{
    [JsonPropertyName("firstName")] public string? FirstName { get; set; }
    [JsonPropertyName("lastName")] public string? LastName { get; set; }
    [JsonPropertyName("identityNumber")] public string? IdentityNumber { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("gsmNumber")] public string? GsmNumber { get; set; }
    [JsonPropertyName("address")] public string? Address { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
}

public sealed class RawCartItemDto
{
    // JsonElement kullanılıyor çünkü orijinal JS kodu id/quantity için gevşek tip kontrolü
    // yapıyor (Number(...) ile zorlama, typeof === "string" ile denetim); bu davranışı
    // birebir taklit etmek için ham JSON değerine ihtiyaç var.
    [JsonPropertyName("id")] public JsonElement Id { get; set; }
    [JsonPropertyName("quantity")] public JsonElement Quantity { get; set; }
}

/// <summary>Doğrulanmış, temizlenmiş alıcı bilgisi (normaliseBuyer'ın karşılığı).</summary>
public sealed record Buyer(
    string FirstName,
    string LastName,
    string IdentityNumber,
    string Email,
    string GsmNumber,
    string Address,
    string City);

/// <summary>Sepetteki tek bir satır (katalog bilgisiyle birleştirilmiş, normaliseCart'ın karşılığı).</summary>
public sealed record CartLine(string Id, int Quantity, string Name, int Price, string Category)
{
    public int LineTotal => Price * Quantity;
}
