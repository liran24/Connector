namespace AiVisibility.Core.Models;

/// <summary>
/// How much a finding hurts the store's visibility to AI shopping agents.
/// </summary>
public enum Severity
{
    /// <summary>The store is invisible to at least one major agent because of this.</summary>
    Critical = 3,

    /// <summary>Agents can see the store but will often skip it in favour of better-described competitors.</summary>
    Important = 2,

    /// <summary>Worth fixing, but agents can work around it.</summary>
    Minor = 1
}
