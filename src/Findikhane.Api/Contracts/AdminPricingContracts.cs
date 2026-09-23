using System.Text.Json.Serialization;

namespace Findikhane.Api.Contracts;

/// <summary>POST /admin/hazelnut-price/override gövdesi.</summary>
public sealed class AdminPriceOverrideRequestDto
{
    [JsonPropertyName("pricePerKg")] public decimal? PricePerKg { get; set; }
    [JsonPropertyName("note")] public string? Note { get; set; }
}
