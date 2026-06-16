namespace IraqiTradeCenterCompany.SharedKernel.Contacts;

public static class ContactKinds
{
    public const string Email = "Email";
    public const string Phone = "Phone";
    public const string Mobile = "Mobile";

    public static bool IsPhoneLike(string kind) =>
        kind is Phone or Mobile;
}

public static class ContactOwnerTypes
{
    public const string User = "User";
    public const string FinancialParty = "FinancialParty";
}
