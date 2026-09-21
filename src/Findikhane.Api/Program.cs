using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Findikhane.Api.Contracts;
using Findikhane.Api.Data;
using Findikhane.Api.Payments;
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

builder.Services.AddSingleton(iyzicoOptions);
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<OrderRepository>();
builder.Services.AddHttpClient<IyzicoClient>(client =>
{
    client.BaseAddress = new Uri(iyzicoOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

var app = builder.Build();

// Sipariş tablosunun var olduğundan emin ol (EF migration yerine basit "ensure schema").
using (var scope = app.Services.CreateScope())
{
    var repository = scope.ServiceProvider.GetRequiredService<OrderRepository>();
    await repository.EnsureSchemaAsync();
}

app.UseDefaultFiles();
app.UseStaticFiles();

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

app.MapPost("/api/checkout", HandleCheckoutAsync);
app.MapPost("/payment/callback", HandlePaymentCallbackAsync);

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
            PaymentStatus = "PENDING"
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
