using System.ComponentModel.DataAnnotations;

namespace AiVisibility.App.Configuration;

/// <summary>
/// The subscription this app charges for.
/// </summary>
/// <remarks>
/// Shopify requires charges to go through its Billing API — taking payment any other way is
/// grounds for removal from the App Store.
/// </remarks>
public sealed class BillingOptions
{
    /// <summary>Configuration section name: <c>Billing</c>.</summary>
    public const string SectionName = "Billing";

    /// <summary>Plan name the merchant sees on the approval screen.</summary>
    [Required(AllowEmptyStrings = false)]
    public string PlanName { get; set; } = "AI Visibility";

    /// <summary>Monthly price. Shopify bills this on the shop's own billing cycle.</summary>
    [Range(0.01, 10_000, ErrorMessage = "Billing:MonthlyPrice must be a positive amount.")]
    public decimal MonthlyPrice { get; set; } = 19m;

    /// <summary>ISO currency code the price is quoted in.</summary>
    [RegularExpression("^[A-Z]{3}$", ErrorMessage = "Billing:Currency must be a three-letter ISO code, e.g. USD.")]
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Free trial length. Zero disables the trial.
    /// </summary>
    [Range(0, 90, ErrorMessage = "Billing:TrialDays must be between 0 and 90.")]
    public int TrialDays { get; set; } = 14;

    /// <summary>
    /// When true, charges are created as test charges: the merchant approves a real-looking
    /// screen but is never billed. Development stores can only ever accept test charges.
    /// </summary>
    public bool UseTestCharges { get; set; } = true;
}
