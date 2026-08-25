using Anthropic;
using Anthropic.Models.Messages;

namespace AiVisibility.Core.Tracking;

/// <summary>
/// Asks Claude a shopper question with web search enabled, and reports what it answered
/// and which sites it cited.
/// </summary>
/// <remarks>
/// This approximates what a shopper sees rather than reproducing it. A consumer chat product
/// applies its own retrieval, ranking and personalisation on top of the model, so treat the
/// output as a directional signal about a store's standing, not a transcript of any real
/// shopper's session. Every tool in this category shares that limitation; the report wording
/// says so rather than implying otherwise.
/// </remarks>
public sealed class ClaudeAssistantProbe : IAssistantProbe
{
    /// <summary>
    /// Keeps answers short and source-backed. Length costs tokens without improving the
    /// signal — all that matters is which stores get named and cited.
    /// </summary>
    private const string SystemPrompt =
        "You are helping a shopper decide where to buy. Search the web, then answer briefly "
        + "and name specific online shops you would actually send them to.";

    private readonly AnthropicClient _client;
    private readonly string _model;
    private readonly int _maxSearches;

    public string Engine => "Claude";

    public ClaudeAssistantProbe(AnthropicClient? client = null, string model = "claude-opus-5", int maxSearches = 3)
    {
        _client = client ?? new AnthropicClient();
        _model = model;
        _maxSearches = maxSearches;
    }

    public async Task<AssistantAnswer> AskAsync(
        ShopperPrompt prompt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                MaxTokens = 2048,
                System = SystemPrompt,
                Tools = [new ToolUnion(new WebSearchTool20260209 { MaxUses = _maxSearches })],
                Messages = [new() { Role = Role.User, Content = prompt.Text }]
            }, cancellationToken: cancellationToken);

            return Read(response);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One engine failing is a gap in the report, not a reason to abandon the run.
            return AssistantAnswer.Failed(Engine, ex.Message);
        }
    }

    /// <summary>
    /// Pulls the prose and the cited sources out of the response.
    /// </summary>
    /// <remarks>
    /// Citations are collected from the text blocks' own citation spans, which record what
    /// the model actually leaned on when writing. Search results it retrieved but never used
    /// are excluded on purpose: a store that merely appeared in a result list was not
    /// recommended to anyone.
    /// </remarks>
    private AssistantAnswer Read(Message response)
    {
        var text = new System.Text.StringBuilder();
        var citations = new List<Citation>();

        foreach (var block in response.Content)
        {
            if (!block.TryPickText(out var textBlock))
            {
                continue;
            }

            text.Append(textBlock.Text);

            foreach (var citation in textBlock.Citations ?? [])
            {
                if (citation.TryPickCitationsWebSearchResultLocation(out var web)
                    && Uri.TryCreate(web.Url, UriKind.Absolute, out var url))
                {
                    citations.Add(new Citation(url!, web.Title));
                }
            }
        }

        return new AssistantAnswer(Engine, text.ToString(), citations);
    }
}
