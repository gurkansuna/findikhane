namespace Findikhane.Api.Catalog;

/// <summary>Ürün bilgisi (fiyat sunucuda tutulur; istemciden gelen fiyat asla güvenilmez).</summary>
public sealed record Product(string Id, string Name, int Price, string Category);

/// <summary>Fındık fiyatlarının en son ne zaman ve hangi kaynaktan güncellendiğine dair bilgi.</summary>
public sealed record PriceUpdateInfo(string Source, decimal KabukluPricePerKg, DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Orijinal server.js içindeki CATALOG sabitinin karşılığı — ürün sayısı bir veritabanı
/// gerektirmeyecek kadar az olduğu için burada da kod içinde tutuluyor. Fiyatlar artık
/// sabit değil: <see cref="Pricing.HazelnutPriceRefreshService"/> tarafından piyasa
/// verisine göre otomatik güncellenir; bu yüzden depolama, atomik bir anlık görüntü
/// (snapshot) değişimiyle thread-safe biçimde yapılıyor (bkz. <see cref="ApplyPrices"/>).
/// </summary>
public static class ProductCatalog
{
    /// <summary>Piyasa verisi hiç alınamazsa (ör. ilk açılış + kaynak erişilemez) kullanılacak başlangıç fiyatları.</summary>
    private static readonly Dictionary<string, Product> SeedItems = new()
    {
        ["giresun-secme"] = new Product("giresun-secme", "Ordu ve Giresun Seçme", 549, "Çiğ iç fındık"),
        ["tas-firin-kavrulmus"] = new Product("tas-firin-kavrulmus", "Taş Fırın Kavrulmuş", 599, "Kavrulmuş iç fındık"),
        ["ipek-kivam"] = new Product("ipek-kivam", "İpek Kıvam", 499, "Katkısız fındık ezmesi")
    };

    private static volatile IReadOnlyDictionary<string, Product> _items = SeedItems;
    private static volatile PriceUpdateInfo? _lastUpdate;

    public static IReadOnlyDictionary<string, Product> Items => _items;

    /// <summary>En son otomatik/manuel fiyat güncellemesinin bilgisi; hiç güncelleme olmadıysa null.</summary>
    public static PriceUpdateInfo? LastUpdate => _lastUpdate;

    public static bool TryGet(string id, out Product product) => _items.TryGetValue(id, out product!);

    /// <summary>
    /// Yeni hesaplanmış fiyatları tüm ürün kataloğuna atomik olarak uygular. Eşzamanlı
    /// isteklerin (checkout, /api/products) hiçbir zaman yarı güncellenmiş bir katalog
    /// görmemesi için önce yeni sözlük tamamen kurulur, sonra referans tek seferde değiştirilir.
    /// </summary>
    public static void ApplyPrices(IReadOnlyDictionary<string, int> newPrices, string source, decimal kabukluPricePerKg, DateTimeOffset updatedAtUtc)
    {
        var current = _items;
        var updated = new Dictionary<string, Product>(current.Count);
        foreach (var (id, product) in current)
        {
            var price = newPrices.TryGetValue(id, out var p) ? p : product.Price;
            updated[id] = product with { Price = price };
        }

        _items = updated;
        _lastUpdate = new PriceUpdateInfo(source, kabukluPricePerKg, updatedAtUtc);
    }
}
