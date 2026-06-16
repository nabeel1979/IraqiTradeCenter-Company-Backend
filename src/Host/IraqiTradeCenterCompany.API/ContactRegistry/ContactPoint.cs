namespace IraqiTradeCenterCompany.API.ContactRegistry;

public class ContactPoint
{
    public long Id { get; set; }
    public string Kind { get; set; } = "";
    public string NormalizedValue { get; set; } = "";
    public string DisplayValue { get; set; } = "";
    public string OwnerType { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
