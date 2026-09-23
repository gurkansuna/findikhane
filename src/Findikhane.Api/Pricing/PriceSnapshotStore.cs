using System.Text.Json;
using System.Text.Json.Serialization;

namespace Findikhane.Api.Pricing;

public sealed record PriceSnapshot(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("kabukluPricePerKg")] decimal KabukluPricePerKg,
    [property: JsonPropertyName("productPrices")] Dictionary<string, int> ProductPrices,
    [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Son başarılı fiyat hesaplamasını diske yazar. Amaç: uygulama yeniden başladığında
/// (deploy, konteyner yeniden başlatma vb.) piyasa kaynağına ilk erişim gerçekleşene
/// kadar geçici olarak <see cref="ProductCatalog"/>'un sabit tohum (seed) fiyatlarına
/// dönmemesi — en son bilinen gerçek fiyatlar hemen geri yüklenir.
/// </summary>
public sealed class PriceSnapshotStore
{
    private readonly string _filePath;
    private readonly ILogger<PriceSnapshotStore> _logger;
    private readonly object _lock = new();

    public PriceSnapshotStore(IHostEnvironment environment, ILogger<PriceSnapshotStore> logger)
    {
        var dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "hazelnut-price-snapshot.json");
        _logger = logger;
    }

    public PriceSnapshot? Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath)) return null;
            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<PriceSnapshot>(json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fiyat anlık görüntüsü okunamadı, yok sayılıyor: {Path}", _filePath);
                return null;
            }
        }
    }

    public void Save(PriceSnapshot snapshot)
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fiyat anlık görüntüsü diske yazılamadı: {Path}", _filePath);
            }
        }
    }
}
