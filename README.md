# Connector — AI Visibility Scanner

Audits a Shopify storefront for how readable it is to AI shopping agents (ChatGPT,
Perplexity, Google AI Mode, Claude), and reports what to fix.

Shoppers increasingly ask an assistant what to buy rather than browsing a store, and an
assistant can only recommend what it can read. This scans a store the way those crawlers
do and produces a 0–100 score with a ranked list of fixes.

## Why it can scan without being installed

The scanner reads Shopify's public `/products.json` endpoint, so it needs no OAuth token
and no app install. A merchant can paste a store URL and get a report — which is what makes
a free, zero-friction audit possible as the top of the funnel.

## What it checks

| Area | Weight | What it establishes |
|---|---|---|
| Crawler access | 35% | Whether `robots.txt` blocks AI crawlers — the one failure that makes everything else moot |
| Structured data | 35% | Whether product pages carry complete schema.org `Product` markup (price, availability, identifiers, ratings) |
| Content depth | 20% | Whether product copy states facts an agent can match a query against, not just sentiment |
| Discovery files | 10% | Whether `sitemap.xml` and `llms.txt` are published and declared |

## Verified, not assumed

The scanner never reports an area it could not read as a pass. A `404` on `robots.txt`
means no rules exist and is a genuine pass; a `403` or a timeout means the file was
withheld, and that area is reported as **not checked** instead. A store that answers
nothing at all is `Unreachable` and gets no score.

This matters commercially, not just technically: stores behind aggressive bot protection
are the ones most likely to be blocking AI crawlers, and an all-clear is the worst possible
answer to give them.

## Visibility tracking

Scanning tells a merchant whether agents *can* read the store. Tracking tells them whether
agents actually *recommend* it — which is the question worth paying for monthly.

It builds shopper questions from the store's own product types, puts them to Claude with web
search on, and reports whether the store got cited and who was cited ahead of it.

Two deliberate design choices:

- **Prompts never name the brand.** Asking an assistant about a store by name proves nothing;
  it will find it. What matters is whether the store surfaces when a shopper describes a need
  and names no brand, because that is the query a new customer types.
- **A citation and a name-drop are counted separately.** An assistant can praise a brand while
  linking the shopper somewhere else. Collapsing the two would inflate the number the merchant
  is paying to trust, so they stay distinct all the way into the report.

This approximates what a shopper sees rather than reproducing it: consumer chat products layer
their own retrieval and ranking on top of the model. It is a trend line, not a transcript —
and the report says so.

## Running it

```bash
# Audit readability. No credentials needed.
dotnet run --project src/AiVisibility.Cli -- scan https://example.myshopify.com 10

# Check whether assistants recommend it. Needs ANTHROPIC_API_KEY; spends credit.
dotnet run --project src/AiVisibility.Cli -- track https://example.myshopify.com 2
```

For `scan`, the trailing number is how many products to sample (default 10) — themes emit the
same markup for every product, so a sample is representative. For `track`, it is how many
product categories to cover (default 2); each category costs three API calls per run, which is
the main lever on what tracking costs to operate.

## Layout

```
src/AiVisibility.Core/Checks      The four readability checks
src/AiVisibility.Core/Tracking    Shopper prompts, assistant probes, mention detection
src/AiVisibility.Cli              Console runner
tests/                            xUnit suite, fixture-driven (no network, no API spend)
```

`IPageFetcher` and `IAssistantProbe` are the only seams that touch the network, so every check
and every piece of mention analysis is tested against fixtures rather than live stores or paid
API calls.

```bash
dotnet test
```

## Status

The scanning engine, the tracking module and the CLI are working and covered by 65 tests.

Two paths have not been exercised end to end from the build environment, which blocks
outbound requests to storefronts and has no API credentials: a successful live scan, and a
live tracking run against the Claude API. Both are covered by fixtures; both are worth a
first real run.

Not yet built: the Shopify app shell — OAuth, webhooks, billing, and the embedded admin UI.
