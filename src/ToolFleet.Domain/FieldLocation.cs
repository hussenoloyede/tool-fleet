namespace ToolFleet.Domain;

/// <summary>A field base where tools are stored and serviced, such as Labuan or Miri.</summary>
public class FieldLocation
{
    private FieldLocation() { } // for EF Core

    public FieldLocation(string name, string country)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Location name is required.");
        if (string.IsNullOrWhiteSpace(country)) throw new DomainException("Country is required.");

        Id = Guid.NewGuid();
        Name = name.Trim();
        Country = country.Trim();
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Country { get; private set; } = "";
}
