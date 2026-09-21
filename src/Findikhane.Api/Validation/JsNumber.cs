using System.Globalization;
using System.Text.Json;

namespace Findikhane.Api.Validation;

/// <summary>
/// JavaScript'in gevşek <c>Number(...)</c> tip zorlamasının, gerekli sınırlar içinde
/// bire bir C# karşılığı. iyzico imza doğrulaması ve sepet miktarı kontrolleri orijinal
/// serviste bu zorlamaya dayandığı için davranış burada aynen korunuyor.
/// </summary>
public static class JsNumber
{
    /// <summary>JS <c>Number(value)</c>: boş/eksik değer 0, sayısal olmayan metin NaN.</summary>
    public static double ToNumber(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDouble(out var d) ? d : double.NaN;
            case JsonValueKind.String:
                var text = element.GetString() ?? "";
                var trimmed = text.Trim();
                if (trimmed.Length == 0) return 0; // Number("") === 0
                return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : double.NaN;
            case JsonValueKind.True:
                return 1;
            case JsonValueKind.False:
                return 0;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return 0; // Number(null) === 0
            default:
                return double.NaN;
        }
    }

    public static double ToNumber(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return 0;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : double.NaN;
    }

    /// <summary>JS <c>Number.isInteger(value)</c>.</summary>
    public static bool IsInteger(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value == Math.Truncate(value);

    /// <summary>
    /// server.js'deki money(value) yardımcısının karşılığı:
    /// Number(value).toFixed(2), sondaki ".00" varsa kırpılır. iyzico imza alanlarının
    /// birleştirilmesinde kullanılır; sayısal olmayan alanlar için "NaN" üretir — bu,
    /// orijinal davranışın kasıtlı olarak korunmuş halidir.
    /// </summary>
    public static string Money(string raw)
    {
        var num = ToNumber(raw);
        var formatted = double.IsNaN(num) ? "NaN" : num.ToString("F2", CultureInfo.InvariantCulture);
        return formatted.EndsWith(".00", StringComparison.Ordinal) ? formatted[..^3] : formatted;
    }
}
