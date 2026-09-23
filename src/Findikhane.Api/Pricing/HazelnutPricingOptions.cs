namespace Findikhane.Api.Pricing;

/// <summary>
/// appsettings.json içindeki "HazelnutPricing" bölümünden bağlanan ayarlar. Türkiye'de
/// fındık fiyatları için resmî/ücretsiz bir API bulunmadığından kaynak, bir web
/// sayfasından düzenli ifadeyle (regex) fiyat çıkaran basit bir "kazıyıcı" (scraper).
/// Sayfa biçimi değişirse yeniden derlemeye gerek kalmadan <see cref="SourceUrl"/> ve
/// <see cref="PricePattern"/> güncellenebilir (appsettings veya ortam değişkeni ile).
/// </summary>
public sealed class HazelnutPricingOptions
{
    public const string SectionName = "HazelnutPricing";

    /// <summary>Kabuklu fındık serbest piyasa fiyatını (TL/kg) içeren, düzenli olarak kontrol edilecek sayfa.</summary>
    public string SourceUrl { get; set; } = "https://tarimmemleketi.com/findik-fiyatlari";

    /// <summary>
    /// Sayfa metninde kg başına TL fiyatını yakalayan, büyük/küçük harf duyarsız düzenli ifade.
    /// İlk yakalama grubu (capture group 1) fiyatı içermelidir (ondalık ayıracı , veya . olabilir).
    /// </summary>
    public string PricePattern { get; set; } = @"fındık\D{0,60}?(\d{2,3}(?:[.,]\d{1,2})?)\s*(?:TL|₺)\s*/?\s*kg";

    /// <summary>Kaç saatte bir otomatik yenileme denenecek (asgari 1 saat).</summary>
    public int RefreshIntervalHours { get; set; } = 24;

    /// <summary>Kabuklu fındıktan iç fındığa dönüşüm çarpanı (verim ~%45-50 olduğundan kg başına ~2.0-2.2x).</summary>
    public double KernelYieldMultiplier { get; set; } = 2.15;

    // Aşağıdaki üç marj, 21 Eylül 2026'da elle yapılan piyasa araştırmasına göre kalibre
    // edildi: o tarihte serbest piyasa kabuklu fındık fiyatı ~185-205 TL/kg (TMO Giresun
    // kalite alım fiyatı 255 TL/kg) bandındaydı ve bu üç ürün için (butik/üretici-direkt
    // karşılaştırmalarla belirlenen) hedef perakende fiyatları 549 / 599 / 499 TL idi.
    // Marjlar, 190 TL/kg'lık bir kabuklu fiyatının tam olarak bu üç rakamı üretecek
    // şekilde geriye doğru hesaplandı (bkz. HazelnutPriceCalculatorTests biçimindeki
    // doğrulama: verify/ klasöründeki manuel test). Piyasa koşulları değiştikçe bu
    // marjların yeniden gözden geçirilmesi gerekir; bunlar fizikî bir yasa değil,
    // başlangıç kalibrasyonudur.

    /// <summary>Çiğ iç fındık (500 g) için iç fındık maliyeti üzerine uygulanacak perakende çarpanı.</summary>
    public double RawMargin { get; set; } = 2.69;

    /// <summary>Taş fırın kavrulmuş (500 g) için iç fındık maliyeti üzerine uygulanacak perakende çarpanı (kavurma işçiliği dahil).</summary>
    public double RoastedMargin { get; set; } = 2.94;

    /// <summary>Fındık ezmesi (500 g) için iç fındık maliyeti üzerine uygulanacak perakende çarpanı (üretim işçiliği/ambalaj dahil).</summary>
    public double PasteMargin { get; set; } = 2.45;

    /// <summary>
    /// Tek bir güncellemede mevcut fiyata göre izin verilen azami değişim oranı (ör. 0.15 = %15).
    /// Kaynak sayfa hatalı/anlık bir değer döndürürse fiyatların aniden çok sapmasını önler.
    /// </summary>
    public double MaxChangeRatio { get; set; } = 0.15;

    /// <summary>
    /// /admin/hazelnut-price/* uçlarını korumak için paylaşılan gizli anahtar (X-Admin-Token başlığı).
    /// Boş bırakılırsa admin uçları tamamen devre dışıdır — üretimde ADMIN_API_KEY ortam
    /// değişkeniyle ayarlanmalı, appsettings.json içine gerçek bir anahtar YAZILMAMALIDIR.
    /// </summary>
    public string AdminApiKey { get; set; } = "";
}
