# AI Visibility

A Shopify app that answers two questions a merchant increasingly needs answered:

1. **Can AI shopping assistants read my store?** — ChatGPT, Perplexity, Google AI Mode, Claude.
2. **Do they actually recommend it?**

Shoppers now ask an assistant what to buy instead of browsing, and an assistant can only
recommend what it can read. This scans a storefront the way those crawlers do, scores it
0–100, lists what to fix in priority order, and then tracks whether the store is being cited
in real answers.

---

## Contents

- [How it works](#how-it-works)
- [Running it](#running-it)
- [Configuration](#configuration)
- [Architecture](#architecture)
- [Security](#security)
- [Testing](#testing)
- [Deployment notes](#deployment-notes)
- [Status and what is left](#status-and-what-is-left)

---

## How it works

### Scanning — can agents read the store?

Four checks, weighted by how much each one actually costs a merchant:

| Area | Weight | What it establishes |
|---|---|---|
| Crawler access | 35% | Whether `robots.txt` blocks AI crawlers — the failure that makes everything else moot |
| Structured data | 35% | Whether product pages carry complete schema.org `Product` markup |
| Content depth | 20% | Whether product copy states facts an agent can match a query against |
| Discovery files | 10% | Whether `sitemap.xml` and `llms.txt` are published and declared |

Crawler access is checked against GPTBot, OAI-SearchBot, ChatGPT-User, PerplexityBot,
Google-Extended, ClaudeBot and others, tiered by how much shopping traffic each drives.
The `robots.txt` parser implements the parts of RFC 9309 that decide visibility: group
matching by user-agent, `Allow`/`Disallow` with `*` and `$` wildcards, and longest-match-wins
with `Allow` breaking ties.

**No install required.** The scanner reads Shopify's public `/products.json`, so it can audit
any store with no OAuth token — which is what makes a zero-friction free audit possible as the
top of the funnel.

### Verified, never assumed

The first live run of the scanner returned **94/100, "well prepared"** for a store where
every request had failed and zero products were read. The original logic treated *could not
read* as *no problem found*.

That is the most damaging thing this tool could do. Stores behind aggressive bot protection
are exactly the ones most likely to be blocking AI crawlers, and they would have received a
clean bill of health.

The fix is structural, and it is the rule the whole codebase follows now:

- Only a `404` on `robots.txt` proves no rules exist. A `403` or a timeout means the file was
  withheld, and the area is reported as **not checked**, never as a pass.
- An area with no readable catalogue is inconclusive, not 100.
- A store that answers nothing gets `Unreachable` and **no score at all**.
- A partial scan renormalises weights across the areas that *were* verified, so it is scored
  on its own terms rather than punished for what could not be read.

### Tracking — do assistants recommend it?

Scanning tells a merchant whether agents *can* read the store. Tracking answers the question
worth paying monthly for.

It builds shopper questions from the store's own product types, puts them to Claude with web
search enabled, and reports whether the store was cited and who was cited ahead of it.

Two decisions worth keeping:

- **Prompts never name the brand.** Asking an assistant about a store by name proves nothing —
  it will find it. The commercially meaningful query is the unbranded one a new customer
  actually types.
- **A citation and a name-drop are counted separately.** An assistant can praise a brand while
  linking the shopper somewhere else. Collapsing the two would inflate the number the merchant
  is paying to trust. Brand matching uses word boundaries and ignores names under three
  characters, because a substring hit would quietly inflate every report.

Tracking *approximates* what a shopper sees rather than reproducing it — consumer chat
products layer their own retrieval and ranking on top of the model. The report says so.

---

## Running it

### The CLI — no credentials needed for scanning

```bash
# Audit any Shopify storefront. No install, no OAuth.
dotnet run --project src/AiVisibility.Cli -- scan https://example.myshopify.com 10

# Check whether assistants recommend it. Needs ANTHROPIC_API_KEY; spends credit.
dotnet run --project src/AiVisibility.Cli -- track https://example.myshopify.com 2
```

For `scan`, the trailing number is how many products to sample (default 10) — themes emit the
same markup for every product, so a sample is representative. For `track`, it is how many
product categories to cover (default 2); **each category costs three API calls per run**,
which is the main lever on what tracking costs to operate, and the number your pricing tiers
should be derived from.

### The Shopify app

```bash
cd src/AiVisibility.App

# The secret must never be committed.
dotnet user-secrets set Shopify:ApiSecret <client-secret>
dotnet user-secrets set Shopify:ApiKey <client-id>

dotnet run
```

Then open `https://<your-tunnel>/shopify/install?shop=<your-dev-store>.myshopify.com`.

You will need a Shopify Partner account, a development store, and a public HTTPS tunnel
(`cloudflared tunnel --url http://localhost:5299` or ngrok). The tunnel URL must match
`Shopify:AppUrl` **and** the App URL in the Partner dashboard exactly, or Shopify rejects the
redirect.

### The dashboard

```bash
cd src/AiVisibility.App/dashboard

npm install
npm run dev        # hot reload on :5173, proxying /api to the backend
npm run build      # type-check, then build into ../wwwroot
npm run typecheck  # types only
```

`dotnet run` serves whatever is in `wwwroot`, so `npm run build` once and the backend serves
the dashboard on the same origin as its API — which is what App Bridge expects.

---

## Configuration

Every setting is documented inline in `src/AiVisibility.App/appsettings.json`, and **all
sections are validated at startup**. A missing secret stops the deployment naming the exact
field:

```
Shopify:ApiSecret is required (Partner dashboard → Client secret).
```

rather than failing a merchant's install hours later.

| Section | What it controls |
|---|---|
| `Shopify` | API key and secret, public app URL, requested scopes, pinned Admin API version |
| `Billing` | Plan name, monthly price, currency, trial length, test-charge mode |
| `ScanSchedule` | Whether recurring scans run, their cron expression (UTC), worker count |
| `Tracking` | Whether visibility tracking is on, its Anthropic API key, categories per run, model |
| `ConnectionStrings:Database` | SQLite locally; a string containing `Host=` switches to PostgreSQL |
| `DataProtection:KeyRingPath` | Where the keys that encrypt stored access tokens live |

Four settings that will bite if you get them wrong:

- **`Shopify:ApiSecret`** — never in `appsettings.json`. Use user-secrets locally and an
  environment variable in production.
- **`Billing:UseTestCharges`** — `true` shows the merchant a real approval screen but never
  bills them. Development stores can *only* accept test charges. Set `false` for production.
- **`DataProtection:KeyRingPath`** — must persist across restarts and be shared by every
  replica. Lose it and every stored access token becomes unreadable, meaning every merchant
  has to reinstall.
- **`Tracking:CategoriesPerRun`** — three API calls per category per run. This is the only
  number that decides what tracking costs you to serve, so derive your pricing tiers from it.

---

## Architecture

```
src/AiVisibility.Core/          The engine. No ASP.NET, no Shopify, no database.
  Checks/                       The four readability checks, behind ICheck
  Robots/                       RFC 9309 parser, AI crawler registry
  StructuredData/               JSON-LD extraction, Product schema grading
  Tracking/                     Shopper prompts, assistant probes, mention detection
  Catalog/                      Reads /products.json

src/AiVisibility.App/           The Shopify app.
  Configuration/                Options classes, validated at startup
  Shopify/                      ShopDomain, signatures, OAuth, session tokens, billing
  Security/                     Access token encryption
  Data/                         EF Core entities and DbContext
  Scanning/                     Ties the engine to the tenant, plus the recurring job
  Endpoints/                    Install, webhooks, dashboard API
  dashboard/                    React + TypeScript, built into wwwroot

src/AiVisibility.Cli/           Console runner for both engines
tests/                          159 xUnit tests across two suites
```

### Dependency injection

Everything is registered in a container and injected through constructors. Nothing in the
app constructs its own dependencies.

The engine ships its own registration extension, so a host adds it in one line:

```csharp
builder.Services.AddAiVisibilityScanner();
```

which registers the scanner, the storefront probe, the HTTP fetcher behind
`IHttpClientFactory`, and **the four checks by contract**:

```csharp
services.AddScoped<ICheck, CrawlerAccessCheck>();
services.AddScoped<ICheck, StructuredDataCheck>();
services.AddScoped<ICheck, ContentDepthCheck>();
services.AddScoped<ICheck, DiscoveryFilesCheck>();
```

`StoreScanner` takes `IEnumerable<ICheck>` and runs whatever it is handed. **It does not know
the names of the checks.** Adding a fifth means registering it and nothing else — there is a
test that registers a check the scanner has never heard of and asserts it gets scored.

`StoreScanner.CreateDefault(fetcher)` exists for the CLI, which has no container.

### Design patterns, and why each one is there

| Pattern | Where | What it buys |
|---|---|---|
| Strategy | `IPageFetcher`, `IAssistantProbe`, `ICheck` | 144 tests run against fixtures — no network, no API spend |
| Null Object | `RobotsTxtParser.AllowAll()` | No `null` robots checks scattered through the checks |
| Value Object | `ShopDomain` | Makes a whole class of security bug unrepresentable |
| Factory Method | `AreaResult.Clean()` / `.Inconclusive()` | The verified/unverified distinction is explicit at every call site |
| Filter (middleware) | `SessionTokenFilter` | Applied to the `/api` group, so a new endpoint is protected by default |
| DTO boundary | `ScanReport` | The engine's types can change without breaking stored reports or the TypeScript client |

### Seams

`IPageFetcher` and `IAssistantProbe` are the only things that touch the network. That is why
the whole suite runs offline in under two seconds and costs nothing in API credit.

---

## Security

Three things carry the weight, because they are where Shopify apps get compromised.

### `ShopDomain` — a type that makes an attack unrepresentable

Shopify passes the shop as a query parameter, and an attacker can put anything there. An app
that forwards it unchecked will send an access token, or an API call, to a host of their
choosing.

Parsing into `ShopDomain` is **the only way** to obtain a shop domain in this codebase, so a
raw string can never reach an outbound request. Fourteen rejection cases are tested, including
suffix attacks (`example.myshopify.com.evil.com`) and userinfo attacks
(`https://evil.com@example.myshopify.com`).

The pattern is anchored with `\z` rather than `$`. In .NET, `$` **also matches immediately
before a trailing newline**, which would let `example.myshopify.com\n` through with the
newline still in the value — a header-injection primitive once that value reaches an outbound
request.

### `ShopifySignature` — two signatures, signed two different ways

Shopify signs OAuth redirects as lowercase hex over the sorted query string, and webhooks as
base64 over the **raw, unparsed** request body. Mixing them up fails silently.

Both comparisons are constant-time; a naive `==` leaks how much of a signature was correct.
`WebhookRequestReader` buffers and verifies **before anything is deserialised**, and is shared
by every webhook endpoint so no handler can skip the check.

### Session tokens — how the embedded dashboard authenticates

The dashboard runs in an iframe where cookies are unreliable. App Bridge mints a short-lived
JWT signed with the app secret; `SessionTokenValidator` checks the signature, the audience,
the lifetime, and the `dest` claim — whose host goes through `ShopDomain` like any other
untrusted value.

The shop is taken **from the token**, never from anything the browser sends. There is no shop
parameter on any API route; one would be an invitation to read another merchant's data.

Validation uses a maintained JWT library rather than hand-rolled parsing. Signature stripping
and algorithm confusion are exactly the mistakes a bespoke implementation makes — there is a
test asserting an `alg: none` token is refused.

### Other

- **Access tokens are encrypted at rest.** A token is full API control of a merchant's store,
  so a leaked database backup must not be a leaked set of storefronts.
- **OAuth CSRF.** A single-use `state` nonce, bound to the shop, ties each install redirect to
  the callback that follows it.
- **All three GDPR webhooks are implemented**, not merely acknowledged — an app missing any of
  them fails App Review. `shop/redact` is a real deletion, carried to stored scans by a cascade
  the tests exercise against a real SQLite engine.

---

## Testing

```bash
dotnet test                                    # 144 tests
cd src/AiVisibility.App/dashboard && npm run typecheck
```

The suites run offline and cost nothing: no live requests, no API spend. Four bugs in this codebase were caught by *running* it rather than by tests, and all four now
have regression coverage:

- The scanner reporting 94/100 for a store it could not read at all.
- The .NET configuration binder **appending** to an options property's default value rather
  than replacing it, which produced `scope=read_products,read_products` on the authorize URL.
- Hangfire's static `RecurringJob` facade reading a global `JobStorage` that a DI-configured
  app never sets, which threw at startup.
- **SQLite refusing to translate `ORDER BY` over a `DateTimeOffset`**, so every "latest" and
  "history" query threw at runtime. The existing tests used SQLite but only inserted and
  deleted rows — they never ran the ordering. `ScanHistoryQueryTests` now reads through the
  service, which is what closes that gap.

That is the argument for running the thing, not only testing it.

---

## Deployment notes

Things that are fine for one instance and will break on two:

- **`EnsureCreated` at startup.** Move to migrations run as a deploy step before scaling out,
  or replicas will race.
- **`InstallStateStore` is in-memory.** A nonce lives for seconds, but a callback landing on a
  different instance would find nothing. Needs sticky sessions or a distributed cache.
- **Hangfire uses in-memory storage.** Swap for `Hangfire.PostgreSql` so a restart does not
  lose the schedule.
- **The Data Protection key ring** must be shared, as above.

---

## Status and what is left

**Working and tested:** the scanning engine, the tracking module, the CLI, the Shopify app
backend (OAuth, webhooks, billing, tenant database, session-token API), the recurring scan
job, and the React dashboard.

**Verified against a running server:** startup validation naming the exact missing field;
install rejecting hostile shop parameters with `400` and redirecting a real shop with `302`;
the OAuth callback rejecting a bad HMAC with `401`; all four webhooks rejecting unsigned
requests with `401` and accepting a correctly signed one with `200`; every `/api` route
rejecting missing and forged bearer tokens with `401`; the dashboard served from `wwwroot`;
Hangfire starting its scheduler.

**Not yet verified end to end** — each needs a credential the build environment does not have:

| Path | What it needs |
|---|---|
| A successful live scan | A storefront the network can reach |
| A live tracking run | `ANTHROPIC_API_KEY` |
| A real OAuth install | A Partner account and a development store |

### Billing and access

Charges go through Shopify's Billing API — billing any other way is grounds for removal from
the App Store, so there is no other path in this codebase.

The flow: the dashboard calls `POST /api/billing/subscribe`, which creates a charge and
returns an approval URL; the merchant approves on Shopify's own screen; Shopify redirects
them to `/billing/confirm`, which is verified by **query HMAC rather than a session token** —
it is a top-level browser redirect, not a call from the embedded app. The charge status is
then read back from Shopify rather than trusted from the redirect, because a URL a merchant
can edit is not evidence that they paid.

`ActiveSubscriptionFilter` guards the routes that spend something: scanning makes a dozen
outbound requests, tracking spends API credit. Read-only routes stay open to a lapsed shop —
they already paid for those reports, and a locked screen is a worse argument for resubscribing
than the reports themselves. The filter returns **402**, which the dashboard uses to show the
subscribe prompt rather than an error.

**Not built yet:**

- The dashboard is a starting point, not a finished product. It is plain React with plain CSS
  rather than a component library: **Shopify's Polaris React is deprecated** in favour of
  Polaris web components, and starting a new app on a retired library is a poor bet. Every
  colour is a token at the top of `styles.css`, so restyling means editing one block.
- Tracking runs only on demand. The recurring job rescans; it does not re-track.
- No email or in-app notification when a score drops.
