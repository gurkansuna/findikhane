using Findikhane.Api.Catalog;

namespace Findikhane.Api.Pricing;

/// <summary>
/// Kabuklu fındığın kg fiyatından, mağazadaki 500 g ürünlerin perakende fiyatlarını
/// hesaplayan saf (yan etkisiz) fonksiyonlar. Girdi/çıktı odaklı tutulduğu için ağ veya
/// disk erişimi olmadan doğrudan test edilebilir.
/// </summary>
public static class HazelnutPriceCalculator
{
    /// <summary>
    /// Kabuklu fındık kg fiyatından ürün bazında 500 g perakende fiyatlarını (TL, tam sayı)
    /// hesaplar. Ürün id'leri <see cref="ProductCatalog"/> ile birebir eşleşir.
    /// </summary>
    public static Dictionary<string, int> CalculateProductPrices(decimal kabukluPricePerKg, HazelnutPricingOptions options)
    {
        var icFindikMaliyetiKg = kabukluPricePerKg * (decimal)options.KernelYieldMultiplier;
        var maliyet500Gr = icFindikMaliyetiKg / 2m;

        return new Dictionary<string, int>
        {
            ["giresun-secme"] = CharmRound(maliyet500Gr * (decimal)options.RawMargin),
            ["tas-firin-kavrulmus"] = CharmRound(maliyet500Gr * (decimal)options.RoastedMargin),
            ["ipek-kivam"] = CharmRound(maliyet500Gr * (decimal)options.PasteMargin)
        };
    }

    /// <summary>
    /// Türk perakende fiyatlandırmasında yaygın olan "9 ile biten" (charm pricing)
    /// yuvarlamayı uygular: en yakın 10'a yuvarlar, sonra 1 çıkarır (ör. 553 → 549).
    /// </summary>
    public static int CharmRound(decimal value)
    {
        if (value < 10) value = 10;
        var roundedToTen = Math.Round(value / 10m, MidpointRounding.AwayFromZero) * 10m;
        var charmed = roundedToTen - 1m;
        return (int)Math.Max(9m, charmed);
    }

    /// <summary>
    /// Yeni hesaplanan fiyatı, mevcut fiyata göre <see cref="HazelnutPricingOptions.MaxChangeRatio"/>
    /// ile sınırlı bir bandın içine sıkıştırır. Kaynak sayfa hatalı/anlık bir sayı
    /// döndürürse (ör. yanlış birim, sayfa hatası) fiyatların tek seferde çok sıçramasını önler.
    /// </summary>
    public static int ClampChange(int currentPrice, int proposedPrice, double maxChangeRatio)
    {
        if (currentPrice <= 0 || maxChangeRatio <= 0) return proposedPrice;

        var maxDelta = currentPrice * maxChangeRatio;
        var lowerBound = currentPrice - maxDelta;
        var upperBound = currentPrice + maxDelta;

        if (proposedPrice < lowerBound) return CharmRound((decimal)lowerBound);
        if (proposedPrice > upperBound) return CharmRound((decimal)upperBound);
        return proposedPrice;
    }
}
