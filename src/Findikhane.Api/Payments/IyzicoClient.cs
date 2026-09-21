using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Findikhane.Api.Validation;

namespace Findikhane.Api.Payments;

/// <summary>
/// server.js'deki createAuthorization / verifyIyzicoSignature / iyzicoRequest
/// fonksiyonlarının birebir C# karşılığı. İmza algoritması iyzico'nun IYZWSv2
/// şemasıyla aynıdır; burada davranış değişikliği yapılmamıştır.
/// </summary>
public sealed class IyzicoClient
{
    private readonly HttpClient _httpClient;
    private readonly IyzicoOptions _options;

    public IyzicoClient(HttpClient httpClient, IyzicoOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public (string Authorization, string RandomKey) CreateAuthorization(string pathname, string body)
    {
        var randomKey = Guid.NewGuid().ToString(); // crypto.randomUUID() ile aynı biçim (küçük harf, tireli)
        var signature = ComputeHmacHex(_options.SecretKey, $"{randomKey}{pathname}{body}");
        var authorizationValue = $"apiKey:{_options.ApiKey}&randomKey:{randomKey}&signature:{signature}";
        var authorization = $"IYZWSv2 {Convert.ToBase64String(Encoding.UTF8.GetBytes(authorizationValue))}";
        return (authorization, randomKey);
    }

    public bool VerifySignature(JsonElement payload, string[] fields)
    {
        if (!payload.TryGetProperty("signature", out var signatureProp) || signatureProp.ValueKind != JsonValueKind.String)
            return false;

        var joined = string.Join(":", fields.Select(field => JsNumber.Money(GetRawFieldText(payload, field))));
        var expected = ComputeHmacHex(_options.SecretKey, joined);
        var actual = (signatureProp.GetString() ?? "").ToLowerInvariant();

        if (actual.Length != expected.Length) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
    }

    public async Task<JsonElement> RequestAsync(string pathname, object payload, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(payload, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        var (authorization, randomKey) = CreateAuthorization(pathname, body);

        using var request = new HttpRequestMessage(HttpMethod.Post, pathname)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", authorization);
        request.Headers.Add("x-iyzi-rnd", randomKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(responseBody);
        }
        catch
        {
            throw new DomainException("iyzico yanıtı okunamadı.");
        }

        var root = document.RootElement;
        var status = root.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
        if (!response.IsSuccessStatusCode || status != "success")
        {
            var errorMessage = root.TryGetProperty("errorMessage", out var errorProp) ? errorProp.GetString() : null;
            throw new DomainException(errorMessage ?? "iyzico ödeme formu başlatılamadı.");
        }

        return root;
    }

    private static string ComputeHmacHex(string secretKey, string data)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GetRawFieldText(JsonElement payload, string field)
    {
        if (!payload.TryGetProperty(field, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => value.GetRawText()
        };
    }
}
