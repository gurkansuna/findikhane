using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Findikhane.Api.Contracts;
using Findikhane.Api.Data;
using Findikhane.Api.Payments;
using Findikhane.Api.Pricing;
using Findikhane.Api.Validation;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// --- Yapılandırma: orijinal server.js gibi doğrudan ortam değişkenlerinden okunuyor ---
var iyzicoOptions = new IyzicoOptions
{
    ApiKey = Environment.GetEnvironmentVariable("IYZICO_API_KEY") ?? "",
    SecretKey = Environment.GetEnvironmentVariable("IYZICO_SECRET_KEY") ?? "",
    BaseUrl = (Environment.GetEnvironmentVariable("IYZICO_BASE_URL") ?? "https://sandbox-api.iyzipay.com").TrimEnd('/'),
    PublicBaseUrl = (Environment.GetEnvironmentVariable("PUBLIC_BASE_URL") ?? "").TrimEnd('/')
};

var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
    ?? "Host=localhost;Port=5432;Database=findikhane;Username=findikhane;Password=findikhane";

var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var parsedPort) ? parsedPort : 8080;
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Fındık fiyatlarının otomatik güncellenmesi: appsettings.json > "HazelnutPricing" bölümünden
// bağlanır; kaynak adresi ve admin anahtarı gibi operasyonel alanlar ortam değişkeniyle
// (kod değiştirmeden) ezilebilir. Detaylar için Pricing/HazelnutPricingOptions.cs.
var pricingOptions = builder.Configuration.GetSection(HazelnutPricingOptions.SectionName).Get<HazelnutPricingOptions>()
    ?? new HazelnutPricingOptions();
pricingOptions.SourceUrl = Environment.GetEnvironmentVariable("HAZELNUT_PRICE_SOURCE_URL") ?? pricingOptions.SourceUrl;
pricingOptions.AdminApiKey = Environment.GetEnvironmentVariable("HAZELNUT_PRICE_ADMIN_KEY") ?? pricingOptions.AdminApiKey;

builder.Services.AddSingleton(iyzicoOptions);
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<OrderRepository>();
builder.Services.AddHttpClient<IyzicoClient>(client =>
{
    client.BaseAddress = new Uri(iyzicoOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddSingleton(pricingOptions);
builder.Services.AddSingleton<ManualPriceOverrideStore>();
builder.Services.AddSingleton<PriceSnapshotStore>();
builder.Services.AddHttpClient<WebHazelnutPriceSource>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddTransient<IHazelnutPriceSource>(sp => sp.GetRequiredService<WebHazelnutPriceSource>());
builder.Services.AddSingleton<HazelnutPriceRefreshService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HazelnutPriceRefreshService>());

var app = builder.Build();

// Sipariş tablosunun var olduğundan emin ol (EF migration yerine basit "ensure schema").
using (var scope = app.Services.CreateScope())
{
    var repository = scope.ServiceProvider.GetRequiredService<OrderRepository>();
    await repository.EnsureSchemaAsync();

    // Konteyner/uygulama az önce yeniden başladıysa, ilk otomatik yenileme çalışana kadar
    // sabit tohum fiyatlarına dönmek yerine diskteki son bilinen fiyatları hemen geri yükle.
    var priceRefresher = scope.ServiceProvider.GetRequiredService<HazelnutPriceRefreshService>();
    priceRefresher.LoadPersistedSnapshotOnStartup();
}

app.UseDefaultFiles();
app.UseStaticFiles();

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

app.MapPost("/api/checkout", HandleCheckoutAsync);
app.MapPost("/payment/callback", HandlePaymentCallbackAsync);
app.MapGet("/api/products", HandleGetProductsAsync);
app.MapPost("/admin/hazelnut-price/refresh", HandleAdminRefreshAsync);
app.MapPost("/admin/hazelnut-price/override", HandleAdminOverrideAsync);
app.MapGet("/admin/orders", HandleAdminOrdersAsync);

// ASP.NET Core'un endpoint eşleştirmesi "/admin" desenini sondaki / olsun ya da olmasın
// (hem "/admin" hem "/admin/") eşleştiriyor — bu yüzden UseStaticFiles/UseDefaultFiles'a
// (bunlar bu isteğe hiç sıra gelmeden önce endpoint zaten eşleşiyor) güvenmek yerine paneli
// burada doğrudan sunuyoruz. Böylece findikhane.com/admin (sonunda / olsun olmasın) her
// zaman çalışır; yönlendirme YAPILMIYOR (aksi halde "/admin/" kendi kendine yönlenip
// tarayıcıda sonsuz yönlendirme/"too many redirects" hatasına yol açardı — bu, ilk
// yazımda gerçek bir hataydı ve Playwright testiyle yakalanıp burada düzeltildi).
// Not: "/admin" deseni zaten hem "/admin" hem "/admin/" isteğiyle eşleşiyor (endpoint
// routing sondaki / karakterine duyarsız); İKİSİNİ BİRDEN ayrıca eşlemeye çalışmak
// "AmbiguousMatchException" ile 500'e yol açar (bunu da Playwright/curl testinde
// yakalayıp düzelttik) — o yüzden burada tek bir MapGet yeterli ve doğru olan.
var adminPanelHtml = File.ReadAllText(Path.Combine(builder.Environment.WebRootPath, "admin", "index.html"));
app.MapGet("/admin", (HttpContext context) => WriteHtmlAsync(context, HttpStatusCode.OK, adminPanelHtml));

app.Run();

// ------------------------------------------------------------------------------------
// server.js#handleCheckout ile birebir aynı akış.
// ------------------------------------------------------------------------------------
async Task HandleCheckoutAsync(HttpContext context, IyzicoClient iyzico, OrderRepository orders, IyzicoOptions options)
{
    if (!options.IsConfigured)
    {
        await WriteJsonAsync(context, HttpStatusCode.ServiceUnavailable, new { error = "Ödeme altyapısı henüz yapılandırılmadı. Lütfen mağaza yöneticisiyle iletişime geçin." }, jsonOptions);
        return;
    }

    try
    {
        var body = await ReadBodyAsync(context.Request, limitBytes: 48 * 1024);

        CheckoutRequestDto? requestData;
        try
        {
            requestData = JsonSerializer.Deserialize<CheckoutRequestDto>(body, jsonOptions);
        }
        catch
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = "Geçersiz istek." }, jsonOptions);
            return;
        }

        var buyer = CheckoutValidator.NormaliseBuyer(requestData?.Buyer);
        var cart = CheckoutValidator.NormaliseCart(requestData?.Items);

        var orderId = $"FH-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{RandomHex(3).ToUpperInvariant()}";
        var total = cart.Sum(item => item.LineTotal);
        var conversationId = Guid.NewGuid().ToString();
        var contactName = $"{buyer.FirstName} {buyer.LastName}";

        var order = new OrderRecord
        {
            OrderId = orderId,
            CreatedAt = DateTimeOffset.UtcNow,
            Cart = cart,
            ConversationId = conversationId,
            Total = total,
            PaymentStatus = "PENDING",
            BuyerName = contactName,
            BuyerPhone = buyer.GsmNumber,
            BuyerEmail = buyer.Email,
            BuyerAddress = buyer.Address,
            BuyerCity = buyer.City
        };
        await orders.InsertAsync(order);

        var address = new Dictionary<string, object?>
        {
            ["address"] = buyer.Address,
            ["contactName"] = contactName,
            ["city"] = buyer.City,
            ["country"] = "Turkey"
        };

        var payload = new Dictionary<string, object?>
        {
            ["locale"] = "tr",
            ["conversationId"] = conversationId,
            ["price"] = total,
            ["paidPrice"] = total,
            ["currency"] = "TRY",
            ["basketId"] = orderId,
            ["paymentGroup"] = "PRODUCT",
            ["callbackUrl"] = $"{options.PublicBaseUrl}/payment/callback",
            ["enabledInstallments"] = new[] { 1, 2, 3, 6, 9 },
            ["buyer"] = new Dictionary<string, object?>
            {
                ["id"] = orderId,
                ["name"] = buyer.FirstName,
                ["surname"] = buyer.LastName,
                ["identityNumber"] = buyer.IdentityNumber,
                ["email"] = buyer.Email,
                ["gsmNumber"] = buyer.GsmNumber,
                ["registrationAddress"] = buyer.Address,
                ["city"] = buyer.City,
                ["country"] = "Turkey",
                ["ip"] = GetRemoteAddress(context.Request)
            },
            ["shippingAddress"] = address,
            ["billingAddress"] = address,
            ["basketItems"] = cart.Select(item => new Dictionary<string, object?>
            {
                ["id"] = item.Id,
                ["price"] = item.Price * item.Quantity,
                ["name"] = item.Quantity == 1 ? item.Name : $"{item.Name} x{item.Quantity}",
                ["category1"] = "Fındık",
                ["category2"] = item.Category,
                ["itemType"] = "PHYSICAL"
            }).ToList()
        };

        var result = await iyzico.RequestAsync("/payment/iyzipos/checkoutform/initialize/auth/ecom", payload, context.RequestAborted);

        if (!iyzico.VerifySignature(result, new[] { "conversationId", "token" }))
        {
            throw new DomainException("Ödeme sağlayıcısının imzası doğrulanamadı.");
        }

        var token = result.GetProperty("token").GetString() ?? "";
        await orders.SetTokenAsync(orderId, token);

        var paymentPageUrl = result.TryGetProperty("paymentPageUrl", out var urlProp) ? urlProp.GetString() : null;
        await WriteJsonAsync(context, HttpStatusCode.OK, new { paymentPageUrl }, jsonOptions);
    }
    catch (DomainException ex)
    {
        await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = ex.Message }, jsonOptions);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Checkout işlenirken beklenmeyen hata");
        await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = ex.Message }, jsonOptions);
    }
}

// ------------------------------------------------------------------------------------
// server.js#handlePaymentCallback ile birebir aynı akış.
// ------------------------------------------------------------------------------------
async Task HandlePaymentCallbackAsync(HttpContext context, IyzicoClient iyzico, OrderRepository orders, IyzicoOptions options)
{
    var body = await ReadBodyAsync(context.Request, limitBytes: 48 * 1024);
    var contentType = context.Request.ContentType ?? "";

    Dictionary<string, string> values;
    if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
    {
        values = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(body))
        {
            using var document = JsonDocument.Parse(body);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                values[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : property.Value.GetRawText();
            }
        }
    }
    else
    {
        values = QueryHelpers.ParseFormEncoded(body);
    }

    var token = values.GetValueOrDefault("token", "");
    if (string.IsNullOrEmpty(token) || !options.IsApiCredentialsPresent())
    {
        await RenderPaymentResultAsync(context, false, "Ödeme sonucu doğrulanamadı.");
        return;
    }

    try
    {
        var order = await orders.FindByTokenAsync(token);
        if (order is null)
        {
            await RenderPaymentResultAsync(context, false, "Sipariş bulunamadı.");
            return;
        }

        var result = await iyzico.RequestAsync("/payment/iyzipos/checkoutform/auth/ecom/detail", new Dictionary<string, object?>
        {
            ["locale"] = "tr",
            ["conversationId"] = order.ConversationId,
            ["token"] = token
        }, context.RequestAborted);

        var fields = new[] { "paymentStatus", "paymentId", "currency", "basketId", "conversationId", "paidPrice", "price", "token" };
        var paymentStatus = result.TryGetProperty("paymentStatus", out var statusProp) ? statusProp.GetString() : null;
        var basketId = result.TryGetProperty("basketId", out var basketProp) ? basketProp.GetString() : null;

        var completed = iyzico.VerifySignature(result, fields) && paymentStatus == "SUCCESS" && basketId == order.OrderId;
        var paymentId = completed && result.TryGetProperty("paymentId", out var paymentIdProp) ? paymentIdProp.GetString() : null;

        await orders.CompletePaymentAsync(order.OrderId, completed, paymentId);
        await RenderPaymentResultAsync(context, completed, completed ? $"Siparişiniz alındı. Sipariş numaranız: {order.OrderId}" : "Ödemeniz tamamlanamadı. Lütfen tekrar deneyin.");
    }
    catch
    {
        await RenderPaymentResultAsync(context, false, "Ödeme sonucu sorgulanırken bir sorun oluştu.");
    }
}

// ------------------------------------------------------------------------------------
// GET /api/products — vitrindeki script.js sayfa yüklenirken bunu çağırır ve DOM'daki
// fiyatları (ve sepet hesaplarını) sunucudaki güncel, otomatik hesaplanmış fiyatlarla
// değiştirir. index.html/script.js içindeki sabit rakamlar yalnızca JS çalışmadan önceki
// ilk boyama (paint) ve olası bir ağ hatası için bir "son çare" (fallback) niteliğindedir.
// ------------------------------------------------------------------------------------
Task HandleGetProductsAsync(HttpContext context)
{
    var lastUpdate = Findikhane.Api.Catalog.ProductCatalog.LastUpdate;
    var payload = new
    {
        products = Findikhane.Api.Catalog.ProductCatalog.Items.Values
            .Select(p => new { p.Id, p.Name, p.Category, p.Price })
            .ToList(),
        lastUpdate = lastUpdate is null ? null : new
        {
            lastUpdate.Source,
            kabukluPricePerKg = lastUpdate.KabukluPricePerKg,
            updatedAtUtc = lastUpdate.UpdatedAtUtc
        }
    };
    return WriteJsonAsync(context, HttpStatusCode.OK, payload, jsonOptions);
}

// ------------------------------------------------------------------------------------
// POST /admin/hazelnut-price/refresh — otomatik döngüyü beklemeden anlık bir yenileme
// tetikler (ör. TMO/serbest piyasa fiyatı yeni açıklandığında). X-Admin-Token başlığı
// HazelnutPricing:AdminApiKey (üretimde HAZELNUT_PRICE_ADMIN_KEY ortam değişkeni) ile
// eşleşmelidir; anahtar boşsa bu uç tamamen kapalıdır.
// ------------------------------------------------------------------------------------
async Task HandleAdminRefreshAsync(HttpContext context, HazelnutPriceRefreshService refresher, HazelnutPricingOptions pricingOpts)
{
    if (!IsAdminAuthorized(context, pricingOpts))
    {
        await WriteJsonAsync(context, HttpStatusCode.Unauthorized, new { error = "Yetkisiz." }, jsonOptions);
        return;
    }

    var result = await refresher.RefreshOnceAsync(context.RequestAborted);
    await WriteJsonAsync(context, result.Success ? HttpStatusCode.OK : HttpStatusCode.BadGateway, new
    {
        result.Success,
        result.Error,
        result.Source,
        result.KabukluPricePerKg,
        result.ProductPrices,
        result.AttemptedAtUtc
    }, jsonOptions);
}

// ------------------------------------------------------------------------------------
// POST /admin/hazelnut-price/override — web kazıyıcı kalıcı olarak bozulduğunda (kaynak
// site tasarımını değiştirdiğinde vb.) kabuklu fındık fiyatını elle sabitler ve fiyatları
// hemen bu değerden yeniden hesaplar. Aynı X-Admin-Token koruması geçerlidir.
// Gövde: { "pricePerKg": 205.0, "note": "kaynak kazıyıcı bozuldu, TMO açıklamasından girildi" }
// ------------------------------------------------------------------------------------
async Task HandleAdminOverrideAsync(HttpContext context, HazelnutPriceRefreshService refresher, ManualPriceOverrideStore overrides, HazelnutPricingOptions pricingOpts)
{
    if (!IsAdminAuthorized(context, pricingOpts))
    {
        await WriteJsonAsync(context, HttpStatusCode.Unauthorized, new { error = "Yetkisiz." }, jsonOptions);
        return;
    }

    var body = await ReadBodyAsync(context.Request, limitBytes: 4 * 1024);

    AdminPriceOverrideRequestDto? requestData;
    try
    {
        requestData = JsonSerializer.Deserialize<AdminPriceOverrideRequestDto>(body, jsonOptions);
    }
    catch
    {
        await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = "Geçersiz istek." }, jsonOptions);
        return;
    }

    if (requestData?.PricePerKg is not { } pricePerKg || pricePerKg <= 0 || pricePerKg > 2000)
    {
        await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = "pricePerKg 0 ile 2000 TL arasında olmalıdır." }, jsonOptions);
        return;
    }

    overrides.Save(pricePerKg, requestData.Note);
    var result = await refresher.RefreshOnceAsync(context.RequestAborted);
    await WriteJsonAsync(context, result.Success ? HttpStatusCode.OK : HttpStatusCode.BadGateway, new
    {
        result.Success,
        result.Error,
        result.Source,
        result.KabukluPricePerKg,
        result.ProductPrices,
        result.AttemptedAtUtc
    }, jsonOptions);
}

// ------------------------------------------------------------------------------------
// GET /admin/orders — /admin panelinin (wwwroot/admin/index.html) veri kaynağı. Geçilen
// siparişleri (kargo/teslimat bilgisiyle birlikte) sayfalanmış şekilde döner. Aynı
// X-Admin-Token koruması geçerlidir (bkz. IsAdminAuthorized) — bu uç kredi kartı bilgisi
// döndürmez (hiç saklanmıyor zaten) ve T.C. kimlik no da hiçbir zaman saklanmadığından
// döndürülemez; yalnızca kargolama için gereken isim/telefon/e-posta/adres/şehir döner.
// Sorgu parametreleri: status (PENDING|SUCCESS|FAILURE, boşsa tümü), q (serbest arama:
// sipariş no/isim/telefon/e-posta), page (1'den başlar), pageSize (azami 200).
// ------------------------------------------------------------------------------------
async Task HandleAdminOrdersAsync(HttpContext context, OrderRepository orders, HazelnutPricingOptions pricingOpts)
{
    if (!IsAdminAuthorized(context, pricingOpts))
    {
        await WriteJsonAsync(context, HttpStatusCode.Unauthorized, new { error = "Yetkisiz." }, jsonOptions);
        return;
    }

    var query = context.Request.Query;
    var status = query.TryGetValue("status", out var statusValues) ? statusValues.ToString() : null;
    if (status is not (null or "" or "PENDING" or "SUCCESS" or "FAILURE"))
    {
        await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = "status PENDING, SUCCESS veya FAILURE olmalıdır." }, jsonOptions);
        return;
    }

    var search = query.TryGetValue("q", out var searchValues) ? searchValues.ToString() : null;

    var page = query.TryGetValue("page", out var pageValues) && int.TryParse(pageValues, out var parsedPage) && parsedPage > 0 ? parsedPage : 1;
    var pageSize = query.TryGetValue("pageSize", out var pageSizeValues) && int.TryParse(pageSizeValues, out var parsedPageSize) && parsedPageSize > 0
        ? Math.Min(parsedPageSize, 200)
        : 50;

    var (records, totalCount) = await orders.GetOrdersAsync(status, search, page, pageSize, context.RequestAborted);

    var payload = new
    {
        orders = records.Select(order => new
        {
            order.OrderId,
            order.CreatedAt,
            order.CompletedAt,
            order.PaymentStatus,
            order.PaymentId,
            order.Total,
            order.BuyerName,
            order.BuyerPhone,
            order.BuyerEmail,
            order.BuyerAddress,
            order.BuyerCity,
            Items = order.Cart.Select(line => new { line.Id, line.Name, line.Quantity, line.Price, line.LineTotal }).ToList()
        }).ToList(),
        totalCount,
        page,
        pageSize
    };
    await WriteJsonAsync(context, HttpStatusCode.OK, payload, jsonOptions);
}

bool IsAdminAuthorized(HttpContext context, HazelnutPricingOptions pricingOpts)
{
    if (string.IsNullOrEmpty(pricingOpts.AdminApiKey)) return false;
    if (!context.Request.Headers.TryGetValue("X-Admin-Token", out var provided) || provided.Count == 0) return false;

    var expectedBytes = Encoding.UTF8.GetBytes(pricingOpts.AdminApiKey);
    var providedBytes = Encoding.UTF8.GetBytes(provided[0] ?? "");
    return expectedBytes.Length == providedBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
}

async Task RenderPaymentResultAsync(HttpContext context, bool success, string message)
{
    var title = success ? "Ödemeniz başarıyla alındı" : "Ödeme tamamlanamadı";
    var color = success ? "#47724e" : "#b44e2d";
    var page = $"""
        <!doctype html><html lang="tr"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{EscapeHtml(title)} | Fındıkhane</title><body style="margin:0;background:#f9f4e9;color:#193d36;font-family:Arial,sans-serif"><main style="max-width:560px;margin:15vh auto;padding:48px;text-align:center"><div style="font-size:48px;color:{color}">{(success ? "✓" : "×")}</div><h1 style="font-family:Georgia,serif;font-size:38px;letter-spacing:-2px">{EscapeHtml(title)}</h1><p style="line-height:1.6">{EscapeHtml(message)}</p><a href="/" style="display:inline-block;background:#193d36;color:white;padding:14px 20px;text-decoration:none;font-weight:bold">Mağazaya dön →</a></main></body></html>
        """;
    await WriteHtmlAsync(context, success ? HttpStatusCode.OK : HttpStatusCode.BadRequest, page);
}

static string EscapeHtml(string value) => value
    .Replace("&", "&amp;")
    .Replace("<", "&lt;")
    .Replace(">", "&gt;")
    .Replace("'", "&#39;")
    .Replace("\"", "&quot;");

static string RandomHex(int byteCount)
{
    var bytes = RandomNumberGenerator.GetBytes(byteCount);
    return Convert.ToHexString(bytes).ToLowerInvariant();
}

static string GetRemoteAddress(HttpRequest request)
{
    if (request.Headers.TryGetValue("X-Forwarded-For", out var forwarded) && forwarded.Count > 0)
    {
        var first = forwarded[0]?.Split(',').FirstOrDefault()?.Trim();
        if (!string.IsNullOrEmpty(first)) return first;
    }
    return request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
}

static async Task<string> ReadBodyAsync(HttpRequest request, int limitBytes)
{
    request.EnableBuffering();
    using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
    var body = await reader.ReadToEndAsync();
    if (Encoding.UTF8.GetByteCount(body) > limitBytes)
    {
        throw new DomainException("İstek boyutu çok büyük.");
    }
    return body;
}

static Task WriteJsonAsync(HttpContext context, HttpStatusCode status, object payload, JsonSerializerOptions options)
{
    ApplySecurityHeaders(context.Response);
    context.Response.StatusCode = (int)status;
    context.Response.ContentType = "application/json; charset=utf-8";
    return context.Response.WriteAsync(JsonSerializer.Serialize(payload, options));
}

static Task WriteHtmlAsync(HttpContext context, HttpStatusCode status, string html)
{
    ApplySecurityHeaders(context.Response);
    context.Response.StatusCode = (int)status;
    context.Response.ContentType = "text/html; charset=utf-8";
    return context.Response.WriteAsync(html);
}

static void ApplySecurityHeaders(HttpResponse response)
{
    response.Headers["Cache-Control"] = "no-store";
    response.Headers["X-Content-Type-Options"] = "nosniff";
    response.Headers["X-Frame-Options"] = "DENY";
    response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
}

// server.js'de PUBLIC_BASE_URL yalnızca checkout'ta kontrol ediliyordu; callback'te
// yalnızca API anahtarlarının varlığı denetleniyordu. Aynı davranış korunuyor.
static class QueryHelpers
{
    public static Dictionary<string, string> ParseFormEncoded(string body)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(body)) return result;
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
            result[key] = value;
        }
        return result;
    }
}
