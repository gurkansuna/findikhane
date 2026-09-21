namespace Findikhane.Api.Validation;

/// <summary>
/// Kullanıcıya doğrudan gösterilmesi güvenli olan, Türkçe hata mesajı taşıyan istisna.
/// server.js'de her `throw new Error("...")` çağrısının karşılığıdır.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
