using System.Text.Json;
using System.Text.Json.Serialization;

namespace Findikhane.Api.Pricing;

public sealed record ManualPriceOverride(
    [property: JsonPropertyName("pricePerKg")] decimal PricePerKg,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("setAtUtc")] DateTimeOffset SetAtUtc);

/// <summary>
/// Web kazıyıcı kalıcı olarak bozulduğunda (kaynak site tasarımını değiştirdiğinde vb.)
/// mağaza yöneticisinin POST /admin/hazelnut-price/override ile elle sabitleyebileceği
/// bir kabuklu fındık fiyatı (TL/kg). Diskteki JSON dosyası, konteyner/uygulama yeniden
/// başlasa bile override'ın kalıcı olmasını sağlar; dosya yoksa override aktif değildir.
/// </summary>
public sealed class ManualPriceOverrideStore
{
    private readonly string _filePath;
    private readonly ILogger<ManualPriceOverrideStore> _logger;
    private readonly object _lock = new();

    public ManualPriceOverrideStore(IHostEnvironment environment, ILogger<ManualPriceOverrideStore> logger)
    {
        var dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "manual-price-override.json");
        _logger = logger;
    }

    public ManualPriceOverride? Read()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath)) return null;
            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<ManualPriceOverride>(json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Manuel fiyat override dosyası okunamadı, yok sayılıyor: {Path}", _filePath);
                return null;
            }
        }
    }

    public void Save(decimal pricePerKg, string? note)
    {
        var value = new ManualPriceOverride(pricePerKg, note, DateTimeOffset.UtcNow);
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (File.Exists(_filePath)) File.Delete(_filePath);
        }
    }
}
