using AiVisibility.Core.Models;

namespace AiVisibility.Core.Checks;

/// <summary>
/// One scored area of a storefront audit.
/// </summary>
/// <remarks>
/// Every check reads the same gathered <see cref="StoreSnapshot"/> and returns one
/// <see cref="AreaResult"/>, so a new check can be added by registering it — the scanner
/// never needs to learn its name.
/// </remarks>
public interface ICheck
{
    /// <summary>Which area this check scores. Also its weight key.</summary>
    ScoreArea Area { get; }

    Task<AreaResult> RunAsync(StoreSnapshot snapshot, CancellationToken cancellationToken = default);
}
