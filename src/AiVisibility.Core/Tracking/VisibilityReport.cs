namespace AiVisibility.Core.Tracking;

/// <summary>One prompt, put to one engine, and what came back.</summary>
public sealed record ProbeOutcome(
    ShopperPrompt Prompt,
    string Engine,
    BrandMention Mention,
    string? Error = null)
{
    public bool Succeeded => Error is null;
}

/// <summary>
/// A competitor the assistants kept sending shoppers to instead.
/// </summary>
/// <param name="Domain">The competitor's site.</param>
/// <param name="TimesCited">How many answers cited it.</param>
/// <param name="TimesAhead">How many answers cited it above this store.</param>
public sealed record Rival(string Domain, int TimesCited, int TimesAhead);

/// <summary>
/// What share of shopper questions actually surfaced the store, and who took its place.
/// </summary>
public sealed record VisibilityReport(
    Uri StoreUrl,
    IReadOnlyList<ProbeOutcome> Outcomes,
    DateTimeOffset RunAt)
{
    /// <summary>Probes that returned an answer; the rest failed and are excluded from rates.</summary>
    public IReadOnlyList<ProbeOutcome> Answered { get; } = Outcomes.Where(o => o.Succeeded).ToList();

    public int FailedProbes => Outcomes.Count - Answered.Count;

    /// <summary>
    /// Share of answers that cited the store. This is the headline number, and it counts
    /// citations only — a brand mentioned in passing did not win the shopper.
    /// </summary>
    public double CitationRate => Rate(o => o.Mention.Rank is not null);

    /// <summary>Share of answers where the store appeared at all, however weakly.</summary>
    public double AnyMentionRate => Rate(o => o.Mention.Mentioned);

    /// <summary>
    /// Average position among cited sites, over the answers that cited the store. Null when
    /// it was never cited.
    /// </summary>
    public double? AverageRank
    {
        get
        {
            var ranks = Answered.Select(o => o.Mention.Rank).OfType<int>().ToList();
            return ranks.Count == 0 ? null : ranks.Average();
        }
    }

    /// <summary>The sites winning these questions, most obstructive first.</summary>
    public IReadOnlyList<Rival> Rivals
    {
        get
        {
            var cited = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var ahead = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var outcome in Answered)
            {
                foreach (var domain in outcome.Mention.CitedDomains)
                {
                    cited[domain] = cited.GetValueOrDefault(domain) + 1;
                }

                foreach (var domain in outcome.Mention.DomainsAhead)
                {
                    ahead[domain] = ahead.GetValueOrDefault(domain) + 1;
                }
            }

            // A store never cited has nothing "ahead" of it, so fall back to raw citation
            // counts — otherwise the merchant losing every question sees an empty list.
            return cited
                .Where(kv => !IsOwnSite(kv.Key))
                .Select(kv => new Rival(kv.Key, kv.Value, ahead.GetValueOrDefault(kv.Key)))
                .OrderByDescending(r => r.TimesAhead)
                .ThenByDescending(r => r.TimesCited)
                .ToList();
        }
    }

    /// <summary>A plain-language summary for the top of the merchant's dashboard.</summary>
    public string Summary
    {
        get
        {
            if (Answered.Count == 0)
            {
                return "No assistant answered, so there is nothing to report yet.";
            }

            var percent = (int)Math.Round(CitationRate * 100);
            var topRival = Rivals.FirstOrDefault();

            return percent switch
            {
                0 when topRival is not null =>
                    $"Not cited in any of the {Answered.Count} answers. {topRival.Domain} was cited in {topRival.TimesCited}.",
                0 => $"Not cited in any of the {Answered.Count} answers.",
                _ => $"Cited in {percent}% of {Answered.Count} answers"
                    + (AverageRank is double rank ? $", typically {rank:0.#} sources in." : ".")
            };
        }
    }

    private bool IsOwnSite(string domain)
    {
        var host = StoreUrl.Host.ToLowerInvariant();
        host = host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;

        return string.Equals(domain, host, StringComparison.OrdinalIgnoreCase)
            || domain.EndsWith("." + host, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
    }

    private double Rate(Func<ProbeOutcome, bool> predicate) =>
        Answered.Count == 0 ? 0 : (double)Answered.Count(predicate) / Answered.Count;
}
