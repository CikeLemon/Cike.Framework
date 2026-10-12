using Core.Plugin.Ddd.Domain;

namespace CQRS.Domain.ValueObjects;

public class Address : ValueObject
{
    public string Province { get; protected set; } = default!;

    public string City { get; protected set; } = default!;

    public string Street { get; protected set; } = default!;

    public string ZipCode { get; protected set; } = default!;

    private Address()
    {
    }

    public Address(string province, string city, string street, string zipCode)
    {
        if (province.IsNullOrWhiteSpace()) throw new UserFriendlyException("Province cannot be empty.");
        if (city.IsNullOrWhiteSpace()) throw new UserFriendlyException("City cannot be empty.");
        if (street.IsNullOrWhiteSpace()) throw new UserFriendlyException("Street cannot be empty.");

        Province = province;
        City = city;
        Street = street;
        ZipCode = zipCode ?? string.Empty;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Province;
        yield return City;
        yield return Street;
        yield return ZipCode;
    }

    public override string ToString()
    {
        return $"{Province}{City}{Street} ({ZipCode})";
    }
}
