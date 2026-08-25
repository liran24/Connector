namespace AiVisibility.Core.Tracking;

/// <summary>
/// Runs a prompt set against one or more assistants and reports where the store stood.
/// </summary>
public sealed class VisibilityTracker
{
    private readonly IReadOnlyList<IAssistantProbe> _probes;
    private readonly BrandMentionDetector _detector;
    private readonly Uri _storeUrl;

    public VisibilityTracker(Uri storeUrl, IEnumerable<string> brandNames, IEnumerable<IAssistantProbe> probes)
    {
        _storeUrl = storeUrl;
        _detector = new BrandMentionDetector(storeUrl, brandNames);
        _probes = probes.ToList();
    }

    /// <summary>
    /// Asks every probe every prompt.
    /// </summary>
    /// <remarks>
    /// Sequential on purpose. These are billed calls against rate-limited endpoints, and a
    /// burst of parallel requests buys a 429 rather than a faster report.
    /// </remarks>
    public async Task<VisibilityReport> RunAsync(
        IReadOnlyList<ShopperPrompt> prompts,
        CancellationToken cancellationToken = default)
    {
        var outcomes = new List<ProbeOutcome>();

        foreach (var probe in _probes)
        {
            foreach (var prompt in prompts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var answer = await probe.AskAsync(prompt, cancellationToken).ConfigureAwait(false);

                outcomes.Add(answer.IsSuccess
                    ? new ProbeOutcome(prompt, probe.Engine, _detector.Analyse(answer))
                    : new ProbeOutcome(
                        prompt,
                        probe.Engine,
                        new BrandMention(MentionEvidence.None, null, Array.Empty<string>(), Array.Empty<string>()),
                        answer.Error));
            }
        }

        return new VisibilityReport(_storeUrl, outcomes, DateTimeOffset.UtcNow);
    }
}
