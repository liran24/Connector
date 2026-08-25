using System.Text.RegularExpressions;

namespace AiVisibility.Core.Tracking;

/// <summary>
/// How firmly an answer actually pointed at the store.
/// </summary>
public enum MentionEvidence
{
    /// <summary>The store did not appear.</summary>
    None,

    /// <summary>The brand name appeared in the prose but no source linked to the store.</summary>
    NameOnly,

    /// <summary>The assistant cited the store's own site.</summary>
    Cited,

    /// <summary>The brand was named and the store was cited.</summary>
    NamedAndCited
}

/// <summary>
/// What one answer said about the store.
/// </summary>
/// <param name="Evidence">How the store appeared, if at all.</param>
/// <param name="Rank">
/// Where the store sat among the sites the assistant cited, 1-based. Null when the store
/// was not cited — prose order is too unreliable to call a rank.
/// </param>
/// <param name="CitedDomains">Every site the answer cited, in the order they appeared.</param>
/// <param name="DomainsAhead">Sites cited before the store.</param>
public sealed record BrandMention(
    MentionEvidence Evidence,
    int? Rank,
    IReadOnlyList<string> CitedDomains,
    IReadOnlyList<string> DomainsAhead)
{
    public bool Mentioned => Evidence != MentionEvidence.None;
}

/// <summary>
/// Decides whether an assistant's answer actually pointed a shopper at a given store.
/// </summary>
/// <remarks>
/// Two independent signals, deliberately weighted differently. A citation of the store's own
/// domain is near-certain evidence. A brand name in prose is weaker: names collide with
/// ordinary words, and an assistant can name a brand while linking a shopper somewhere else
/// entirely. Reporting those as equivalent would inflate the numbers a merchant is paying to
/// trust, so they stay distinguishable all the way into the report.
/// </remarks>
public sealed class BrandMentionDetector
{
    private readonly string _storeHost;
    private readonly IReadOnlyList<Regex> _brandPatterns;

    /// <param name="storeUrl">The store's own URL; its host is what counts as a citation.</param>
    /// <param name="brandNames">
    /// Names the store trades under. Blank entries are dropped, and names shorter than three
    /// characters are ignored outright — a two-letter brand matches too much prose to be
    /// evidence of anything.
    /// </param>
    public BrandMentionDetector(Uri storeUrl, IEnumerable<string> brandNames)
    {
        _storeHost = Normalise(storeUrl.Host);

        _brandPatterns = brandNames
            .Where(name => !string.IsNullOrWhiteSpace(name) && name.Trim().Length >= 3)
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(BuildPattern)
            .ToList();
    }

    public BrandMention Analyse(AssistantAnswer answer)
    {
        var citedDomains = new List<string>();

        foreach (var citation in answer.Citations)
        {
            var host = Normalise(citation.Url.Host);
            if (!citedDomains.Contains(host, StringComparer.OrdinalIgnoreCase))
            {
                citedDomains.Add(host);
            }
        }

        var storeIndex = citedDomains.FindIndex(IsStore);
        var named = _brandPatterns.Any(pattern => pattern.IsMatch(answer.Text));

        var evidence = (storeIndex >= 0, named) switch
        {
            (true, true) => MentionEvidence.NamedAndCited,
            (true, false) => MentionEvidence.Cited,
            (false, true) => MentionEvidence.NameOnly,
            _ => MentionEvidence.None
        };

        return new BrandMention(
            evidence,
            storeIndex >= 0 ? storeIndex + 1 : null,
            citedDomains,
            storeIndex >= 0 ? citedDomains.Take(storeIndex).ToList() : Array.Empty<string>());
    }

    /// <summary>
    /// Treats a host as the store's when it matches exactly or is a subdomain of it, so a
    /// citation of shop.example.com counts for a store at example.com.
    /// </summary>
    private bool IsStore(string host) =>
        string.Equals(host, _storeHost, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("." + _storeHost, StringComparison.OrdinalIgnoreCase)
        || _storeHost.EndsWith("." + host, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Matches the brand as whole words, so "Ada" cannot be found inside "adaptor", while
    /// still allowing punctuation and possessives around it.
    /// </summary>
    private static Regex BuildPattern(string brand)
    {
        // Internal whitespace is flexible: "Death Wish" should match "Death  Wish".
        var body = string.Join(@"\s+", brand.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(Regex.Escape));

        return new Regex($@"(?<![\p{{L}}\p{{N}}]){body}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    }

    private static string Normalise(string host)
    {
        host = host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
}
