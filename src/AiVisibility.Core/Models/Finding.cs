namespace AiVisibility.Core.Models;

/// <summary>
/// A single actionable problem found while scanning a store.
/// </summary>
/// <param name="Code">Stable identifier, used for de-duplication and for linking to a fix.</param>
/// <param name="Severity">How badly this hurts agent visibility.</param>
/// <param name="Title">One-line summary shown in the merchant dashboard.</param>
/// <param name="Detail">What was actually observed, including the evidence.</param>
/// <param name="Fix">What the merchant (or the app) should do about it.</param>
/// <param name="Url">The page or file the finding was observed on, when applicable.</param>
public sealed record Finding(
    string Code,
    Severity Severity,
    string Title,
    string Detail,
    string Fix,
    string? Url = null);
