using System.ComponentModel.DataAnnotations;

namespace AiVisibility.App.Configuration;

/// <summary>
/// How often paying shops are rescanned in the background.
/// </summary>
public sealed class ScanScheduleOptions
{
    /// <summary>Configuration section name: <c>ScanSchedule</c>.</summary>
    public const string SectionName = "ScanSchedule";

    /// <summary>
    /// Whether the recurring scan runs at all. Turn it off in development so a local run does
    /// not start making requests to real storefronts.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Cron expression for the rescan, in UTC.
    /// </summary>
    /// <remarks>
    /// Weekly by default. Storefront markup rarely changes daily, and each run costs real
    /// requests against merchants' shops — a faster schedule buys noise, not insight.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "ScanSchedule:Cron is required, e.g. '0 3 * * 1' for 03:00 UTC every Monday.")]
    public string Cron { get; set; } = "0 3 * * 1";

    /// <summary>
    /// Number of background workers. One is right until there are enough shops that a weekly
    /// pass no longer finishes comfortably.
    /// </summary>
    [Range(1, 16, ErrorMessage = "ScanSchedule:WorkerCount must be between 1 and 16.")]
    public int WorkerCount { get; set; } = 1;
}
