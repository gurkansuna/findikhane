using Findikhane.Api.Catalog;

namespace Findikhane.Api.Pricing;

/// <summary>Bir fiyat yenileme denemesinin sonucu — hem zamanlanmış hem admin-tetikli çağrılarda kullanılır.</summary>
public sealed record PriceRefreshResult(
    bool Success,
    string? Error,
    string? Source,
    decimal? KabukluPricePerKg,
    IReadOnlyDictionary<string, int>? ProductPrices,
    DateTimeOffset AttemptedAtUtc);

/// <summary>
/// Fındık fiyatlarını otomatik güncel tutan arka plan servisi. Açılışta bir kez, ardından
/// <see cref="HazelnutPricingOptions.RefreshIntervalHours"/> aralığında çalışır. Ayrıca
/// POST /admin/hazelnut-price/refresh ve /override uçları da aynı <see cref="RefreshOnceAsync"/>
/// metodunu doğrudan çağırarak anlık, isteğe bağlı bir yenileme tetikleyebilir.
///
/// Sıra: önce elle ayarlanmış bir override var mı bakılır (varsa o esas alınır — web
/// kaynağı hiç sorgulanmaz); yoksa web kaynağından okunur. İkisi de başarısız olursa
/// mevcut fiyatlara dokunulmaz ve bir uyarı loglanır — sitenin fiyatsız/bozuk kalması
/// yerine "son bilinen iyi durum" korunur.
/// </summary>
public sealed class HazelnutPriceRefreshService : BackgroundService
{
    private readonly IHazelnutPriceSource _webSource;
    private readonly ManualPriceOverrideStore _overrideStore;
    private readonly PriceSnapshotStore _snapshotStore;
    private readonly HazelnutPricingOptions _options;
    private readonly ILogger<HazelnutPriceRefreshService> _logger;

    public HazelnutPriceRefreshService(
        IHazelnutPriceSource webSource,
        ManualPriceOverrideStore overrideStore,
        PriceSnapshotStore snapshotStore,
        HazelnutPricingOptions options,
        ILogger<HazelnutPriceRefreshService> logger)
    {
        _webSource = webSource;
        _overrideStore = overrideStore;
        _snapshotStore = snapshotStore;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Uygulama açılışında, ilk otomatik yenileme henüz çalışmadan önce diskteki son bilinen
    /// fiyat anlık görüntüsünü <see cref="ProductCatalog"/>'a yükler. Program.cs'de
    /// <c>app.Build()</c> sonrasında, <c>app.Run()</c>'dan önce bir kez çağrılmalıdır.
    /// </summary>
    public void LoadPersistedSnapshotOnStartup()
    {
        var snapshot = _snapshotStore.Load();
        if (snapshot is null) return;

        ProductCatalog.ApplyPrices(snapshot.ProductPrices, snapshot.Source, snapshot.KabukluPricePerKg, snapshot.UpdatedAtUtc);
        _logger.LogInformation(
            "Kayıtlı fiyat anlık görüntüsü yüklendi: kaynak={Source}, kabuklu={Price} TL/kg, güncelleme={UpdatedAt:u}",
            snapshot.Source, snapshot.KabukluPricePerKg, snapshot.UpdatedAtUtc);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Açılışta hemen bir deneme yap; sonrasında düzenli aralıklarla tekrarla.
        await RefreshOnceAsync(stoppingToken);

        var intervalHours = Math.Max(1, _options.RefreshIntervalHours);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(intervalHours));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RefreshOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Uygulama kapanıyor; sessizce çık.
        }
    }

    public async Task<PriceRefreshResult> RefreshOnceAsync(CancellationToken cancellationToken)
    {
        var attemptedAt = DateTimeOffset.UtcNow;

        decimal? price;
        string source;

        var manualOverride = _overrideStore.Read();
        if (manualOverride is not null)
        {
            price = manualOverride.PricePerKg;
            source = "manual-override";
            _logger.LogInformation(
                "Manuel fiyat override kullanılıyor: {Price} TL/kg (ayarlanma: {SetAt:u}{Note})",
                price, manualOverride.SetAtUtc,
                string.IsNullOrWhiteSpace(manualOverride.Note) ? "" : $", not: {manualOverride.Note}");
        }
        else
        {
            price = await _webSource.GetKabukluPricePerKilogramAsync(cancellationToken);
            source = _webSource.Name;
        }

        if (price is null)
        {
            _logger.LogWarning("Fındık fiyatı hiçbir kaynaktan alınamadı; mevcut ürün fiyatları değiştirilmeden korunuyor.");
            return new PriceRefreshResult(false, "Fiyat kaynağından geçerli bir değer alınamadı.", null, null, null, attemptedAt);
        }

        var proposed = HazelnutPriceCalculator.CalculateProductPrices(price.Value, _options);

        var clamped = new Dictionary<string, int>();
        foreach (var (id, proposedPrice) in proposed)
        {
            var currentPrice = ProductCatalog.TryGet(id, out var product) ? product.Price : proposedPrice;
            clamped[id] = HazelnutPriceCalculator.ClampChange(currentPrice, proposedPrice, _options.MaxChangeRatio);
        }

        ProductCatalog.ApplyPrices(clamped, source, price.Value, attemptedAt);
        _snapshotStore.Save(new PriceSnapshot(source, price.Value, clamped, attemptedAt));

        _logger.LogInformation(
            "Fındık fiyatları güncellendi (kaynak: {Source}, kabuklu: {Price} TL/kg): {Prices}",
            source, price.Value, string.Join(", ", clamped.Select(kv => $"{kv.Key}={kv.Value}")));

        return new PriceRefreshResult(true, null, source, price.Value, clamped, attemptedAt);
    }
}
