namespace IraqiTradeCenterCompany.API.Auth;

public class CompanyUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "Admin";
    public bool IsActive { get; set; } = true;
    /// <summary>يُفرض على المستخدم تغيير كلمة المرور عند الدخول التالي (كلمات مؤقتة من الأدمن).</summary>
    public bool MustChangePassword { get; set; }
    /// <summary>صورة المستخدم (Base64 data URL) — اختياري.</summary>
    public string? AvatarBase64 { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>JSON صغيرة لتخزين تفضيلات المستخدم (طي/إخفاء الأقسام، تفضيلات الواجهة...). NULL = لم يضبطها بعد.</summary>
    public string? Preferences { get; set; }
}
