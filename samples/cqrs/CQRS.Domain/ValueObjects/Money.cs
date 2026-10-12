using Core.Plugin.Ddd.Domain;

namespace CQRS.Domain.ValueObjects;

public class Money : ValueObject
{
    public const string DefaultCurrency = "CNY";

    public decimal Amount { get; protected set; }

    public string Currency { get; protected set; } = DefaultCurrency;

    private Money()
    {
    }

    public Money(decimal amount, string currency = DefaultCurrency)
    {
        if (amount < 0) throw new UserFriendlyException("Amount cannot be negative.");
        if (currency.IsNullOrWhiteSpace()) throw new UserFriendlyException("Currency cannot be empty.");

        Amount = amount;
        Currency = currency;
    }

    public Money Add(Money other)
    {
        AssertSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        AssertSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    private void AssertSameCurrency(Money other)
    {
        if (Currency != other.Currency)
        {
            throw new UserFriendlyException($"Currency mismatch: {Currency} vs {other.Currency}.");
        }
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public static Money operator +(Money a, Money b) => a.Add(b);

    public static Money operator -(Money a, Money b) => a.Subtract(b);

    public override string ToString()
    {
        return $"{Amount:0.##} {Currency}";
    }
}
