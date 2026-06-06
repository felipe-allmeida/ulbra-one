namespace Ulbra.One.Common.Application.Ids;

/// <summary>
/// A Stripe-style prefixed, strongly-typed id wrapping a <see cref="Guid"/>.
/// Serializes on the wire as <c>{prefix}{guid:N}</c> (e.g. <c>company_3fa85f64...</c>).
/// </summary>
public interface IPrefixedId
{
    Guid Value { get; }

    static abstract string Prefix { get; }
}
