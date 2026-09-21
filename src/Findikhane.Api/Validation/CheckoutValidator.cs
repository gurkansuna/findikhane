using System.Text.Json;
using System.Text.RegularExpressions;
using Findikhane.Api.Catalog;
using Findikhane.Api.Contracts;

namespace Findikhane.Api.Validation;

/// <summary>
/// server.js içindeki cleanText / normaliseBuyer / normaliseCart fonksiyonlarının
/// birebir C# karşılığı. Hata mesajları bilerek Türkçe ve orijinaliyle aynı bırakıldı;
/// bu mesajlar doğrudan kullanıcıya (checkout formunun altında) gösteriliyor.
/// </summary>
public static class CheckoutValidator
{
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex IdentityNumberPattern = new(@"^\d{11}$", RegexOptions.Compiled);
    private static readonly Regex EmailPattern = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);
    private static readonly Regex GsmPattern = new(@"^\+?90?5\d{9}$", RegexOptions.Compiled);
    private static readonly Regex GsmStripPattern = new(@"[\s()\-]", RegexOptions.Compiled);

    public static string CleanText(string? value, string field, int maxLength = 120)
    {
        if (value is null) throw new DomainException($"{field} zorunludur.");
        var cleaned = WhitespaceRun.Replace(value.Trim(), " ");
        if (cleaned.Length == 0 || cleaned.Length > maxLength) throw new DomainException($"{field} geçerli değil.");
        return cleaned;
    }

    public static Buyer NormaliseBuyer(RawBuyerDto? raw)
    {
        raw ??= new RawBuyerDto();

        var firstName = CleanText(raw.FirstName, "Ad");
        var lastName = CleanText(raw.LastName, "Soyad");
        var identityNumber = CleanText(raw.IdentityNumber, "T.C. kimlik no", 11);
        var email = CleanText(raw.Email, "E-posta", 254).ToLowerInvariant();
        var gsmNumber = GsmStripPattern.Replace(CleanText(raw.GsmNumber, "Telefon", 16), "");
        var address = CleanText(raw.Address, "Teslimat adresi", 255);
        var city = CleanText(raw.City, "Şehir", 64);

        if (!IdentityNumberPattern.IsMatch(identityNumber))
            throw new DomainException("T.C. kimlik no 11 rakam olmalıdır.");
        if (!EmailPattern.IsMatch(email))
            throw new DomainException("E-posta geçerli değil.");

        var gsmForCheck = gsmNumber.StartsWith("00", StringComparison.Ordinal) ? "+" + gsmNumber[2..] : gsmNumber;
        if (!GsmPattern.IsMatch(gsmForCheck))
            throw new DomainException("Telefon +905XXXXXXXXX biçiminde olmalıdır.");

        var normalisedGsm = gsmNumber.StartsWith("+", StringComparison.Ordinal) ? gsmNumber : "+" + gsmNumber;

        return new Buyer(firstName, lastName, identityNumber, email, normalisedGsm, address, city);
    }

    public static List<CartLine> NormaliseCart(List<RawCartItemDto>? rawItems)
    {
        if (rawItems is null || rawItems.Count == 0) throw new DomainException("Sepetiniz boş.");

        var quantities = new Dictionary<string, int>();
        var order = new List<string>(); // ilk görülme sırasını korumak için (Map davranışı)

        foreach (var item in rawItems)
        {
            var id = item.Id.ValueKind == JsonValueKind.String ? item.Id.GetString() ?? "" : "";
            var quantity = JsNumber.ToNumber(item.Quantity);

            if (!ProductCatalog.TryGet(id, out _) || !JsNumber.IsInteger(quantity) || quantity < 1 || quantity > 20)
                throw new DomainException("Sepet bilgisi geçerli değil.");

            var intQuantity = (int)quantity;
            if (!quantities.ContainsKey(id)) order.Add(id);
            quantities[id] = Math.Min(quantities.GetValueOrDefault(id, 0) + intQuantity, 20);
        }

        return order.Select(id =>
        {
            ProductCatalog.TryGet(id, out var product);
            return new CartLine(id, quantities[id], product.Name, product.Price, product.Category);
        }).ToList();
    }
}
