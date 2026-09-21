namespace Findikhane.Api.Catalog;

/// <summary>Ürün bilgisi (fiyat sunucuda sabittir; istemciden gelen fiyat asla güvenilmez).</summary>
public sealed record Product(string Id, string Name, int Price, string Category);

/// <summary>
/// Orijinal server.js içindeki CATALOG sabitinin doğrudan karşılığı.
/// Ürünler bir veritabanı gerektirmeyecek kadar az ve sabit olduğu için burada da
/// kod içinde sabit tutuluyor; sipariş geçmişi ise PostgreSQL'de saklanıyor.
/// </summary>
public static class ProductCatalog
{
    public static readonly IReadOnlyDictionary<string, Product> Items = new Dictionary<string, Product>
    {
        ["giresun-secme"] = new Product("giresun-secme", "Ordu ve Giresun Seçme", 349, "Çiğ iç fındık"),
        ["tas-firin-kavrulmus"] = new Product("tas-firin-kavrulmus", "Taş Fırın Kavrulmuş", 389, "Kavrulmuş iç fındık"),
        ["ipek-kivam"] = new Product("ipek-kivam", "İpek Kıvam", 269, "Katkısız fındık ezmesi")
    };

    public static bool TryGet(string id, out Product product) => Items.TryGetValue(id, out product!);
}
