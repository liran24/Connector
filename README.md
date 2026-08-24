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

## Running it

```bash
dotnet run --project src/AiVisibility.Cli -- https://example.myshopify.com 10
```

The second argument is how many products to sample (default 10). Themes emit the same
markup for every product, so a sample is representative.

## Layout

```
src/AiVisibility.Core   Scanning engine — no I/O beyond IPageFetcher, fully testable
src/AiVisibility.Cli    Console runner
tests/                  xUnit suite, fixture-driven (no network)
```

`IPageFetcher` is the only seam that touches the network, so every check is tested against
fixtures rather than live stores.

```bash
dotnet test
```

## Status

The scanning engine and CLI are working and covered by tests. Not yet built: the Shopify
app shell (OAuth, webhooks, billing, embedded admin UI) and the visibility-tracking module
that queries assistants to see whether a store is actually being recommended.
