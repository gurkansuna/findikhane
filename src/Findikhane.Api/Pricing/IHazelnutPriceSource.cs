namespace Findikhane.Api.Pricing;

/// <summary>Kabuklu fındığın güncel serbest piyasa fiyatını (TL/kg) sağlayan bir kaynak.</summary>
public interface IHazelnutPriceSource
{
    /// <summary>Loglarda ve /api/products yanıtında görünecek, kaynağı tanımlayan kısa ad.</summary>
    string Name { get; }

    /// <summary>
    /// Güncel kg fiyatını döndürür; kaynak erişilemezse, sayfa biçimi değiştiyse ya da
    /// ayrıştırma başarısız olursa <c>null</c> döner — hiçbir zaman istisna fırlatmaz,
    /// böylece çağıran taraf her zaman "mevcut fiyatı koru" yoluna güvenle düşebilir.
    /// </summary>
    Task<decimal?> GetKabukluPricePerKilogramAsync(CancellationToken cancellationToken);
}
