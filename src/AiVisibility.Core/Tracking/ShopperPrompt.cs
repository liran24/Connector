namespace AiVisibility.Core.Tracking;

/// <summary>
/// A question a real shopper would put to an assistant, used to probe whether a store
/// surfaces in the answer.
/// </summary>
/// <param name="Text">The question as it would be typed.</param>
/// <param name="Category">What the prompt is testing, for grouping in the report.</param>
public sealed record ShopperPrompt(string Text, string Category);

/// <summary>A source an assistant cited when answering.</summary>
public sealed record Citation(Uri Url, string? Title);

/// <summary>
/// One assistant's answer to one prompt. A failed probe is a result, not an exception —
/// a rate-limited engine should not sink the whole tracking run.
/// </summary>
public sealed record AssistantAnswer(
    string Engine,
    string Text,
    IReadOnlyList<Citation> Citations,
    string? Error = null)
{
    public bool IsSuccess => Error is null;

    public static AssistantAnswer Failed(string engine, string error) =>
        new(engine, string.Empty, Array.Empty<Citation>(), error);
}

/// <summary>
/// Asks an assistant a shopper question and returns what it said, with sources.
/// </summary>
/// <remarks>
/// Abstracted per engine so ChatGPT, Perplexity and Claude can be tracked side by side,
/// and so the analysis layer can be tested without spending API credit.
/// </remarks>
public interface IAssistantProbe
{
    /// <summary>Name shown in the report, e.g. "Claude".</summary>
    string Engine { get; }

    Task<AssistantAnswer> AskAsync(ShopperPrompt prompt, CancellationToken cancellationToken = default);
}
