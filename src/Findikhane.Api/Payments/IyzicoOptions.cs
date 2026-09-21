namespace Findikhane.Api.Payments;

public sealed class IyzicoOptions
{
    public string ApiKey { get; init; } = "";
    public string SecretKey { get; init; } = "";
    public string BaseUrl { get; init; } = "https://sandbox-api.iyzipay.com";
    public string PublicBaseUrl { get; init; } = "";

    public bool IsConfigured => !string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(SecretKey) && !string.IsNullOrEmpty(PublicBaseUrl);

    /// <summary>server.js#handlePaymentCallback yalnızca API anahtarlarının varlığını kontrol eder (PUBLIC_BASE_URL hariç).</summary>
    public bool IsApiCredentialsPresent() => !string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(SecretKey);
}
