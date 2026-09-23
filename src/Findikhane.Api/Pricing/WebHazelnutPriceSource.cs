using System.Globalization;
using System.Text.RegularExpressions;

namespace Findikhane.Api.Pricing;

/// <summary>
/// <see cref="HazelnutPricingOptions.SourceUrl"/> adresindeki sayfayı çeker ve
/// <see cref="HazelnutPricingOptions.PricePattern"/> ile kg başına TL fiyatını ayıklar.
///
/// ÖNEMLİ SINIRLAMA: Türkiye'de fındık fiyatları için resmî/ücretsiz bir API yok; bu
/// yüzden burada bir haber/veri sitesinin HTML çıktısı "kazınıyor" (scraping). Bu doğası
/// gereği kırılgandır — kaynak site tasarımını değiştirirse bu yöntem fiyat bulamaz hale
/// gelebilir. Bu durumda sistem çökmez: <see cref="HazelnutPriceRefreshService"/> son
/// bilinen fiyatı korur ve bir uyarı loglar. Kaynak kalıcı olarak bozulursa
/// POST /admin/hazelnut-price/override ile fiyat elle ayarlanabilir (bkz. README).
/// </summary>
public sealed class WebHazelnutPriceSource : IHazelnutPriceSource
{
    private readonly HttpClient _httpClient;
    private readonly HazelnutPricingOptions _options;
    private readonly ILogger<WebHazelnutPriceSource> _logger;

    public string Name => "web-scrape";

    public WebHazelnutPriceSource(HttpClient httpClient, HazelnutPricingOptions options, ILogger<WebHazelnutPriceSource> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<decimal?> GetKabukluPricePerKilogramAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SourceUrl))
        {
            _logger.LogWarning("HazelnutPricing:SourceUrl boş; web kaynağı atlanıyor.");
            return null;
        }

        string html;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _options.SourceUrl);
            // Bazı siteler tarayıcı benzeri bir User-Agent olmadan farklı (veya boş) içerik döndürüyor.
            request.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (compatible; FindikhaneFiyatBotu/1.0; +https://findikhane.example)");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Fındık fiyatı kaynağı {Url} HTTP {Status} döndürdü.", _options.SourceUrl, (int)response.StatusCode);
                return null;
            }

            html = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Fındık fiyatı kaynağı {Url} adresine erişilemedi.", _options.SourceUrl);
            return null;
        }

        return ExtractPrice(html);
    }

    /// <summary>
    /// Ayrıştırma mantığı ayrı bir metotta tutuluyor ki gerçek bir ağ isteği olmadan
    /// (örn. kaydedilmiş bir örnek HTML/metin ile) birim test edilebilsin. Genel (public)
    /// tutuluyor çünkü sözleşmesinin (bir metinden fiyat çıkarma) kendisi bir sızıntı değil.
    /// </summary>
    public decimal? ExtractPrice(string pageText)
    {
        Regex pattern;
        try
        {
            pattern = new Regex(_options.PricePattern, RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "HazelnutPricing:PricePattern geçersiz bir düzenli ifade: {Pattern}", _options.PricePattern);
            return null;
        }

        Match match;
        try
        {
            match = pattern.Match(pageText);
        }
        catch (RegexMatchTimeoutException)
        {
            _logger.LogWarning("Fındık fiyatı deseni sayfa üzerinde eşleşme zaman aşımına uğradı.");
            return null;
        }

        if (!match.Success || match.Groups.Count < 2)
        {
            _logger.LogWarning("Fındık fiyatı deseni sayfa içeriğinde eşleşmedi; kaynak sayfa biçimi değişmiş olabilir.");
            return null;
        }

        var raw = match.Groups[1].Value.Replace(',', '.');
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price <= 0)
        {
            _logger.LogWarning("Yakalanan fiyat metni sayıya çevrilemedi: {Raw}", raw);
            return null;
        }

        // Kabuklu fındık serbest piyasa fiyatı için akla yatkın bir bant (TL/kg): bunun dışına
        // çıkan bir eşleşme muhtemelen yanlış bir sayıyı (yıl, yüzde, sayfa numarası vb.) yakalamıştır.
        if (price is < 30 or > 2000)
        {
            _logger.LogWarning("Yakalanan fiyat akla yatkın aralığın dışında, güvenilmiyor: {Price}", price);
            return null;
        }

        return price;
    }
}
