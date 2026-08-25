import { useCallback, useEffect, useState } from "react";
import { api, isEmbedded } from "./api";
import { AreaBreakdown } from "./components/AreaBreakdown";
import { FindingsList } from "./components/FindingsList";
import { ScoreCard } from "./components/ScoreCard";
import { ScoreTrend } from "./components/ScoreTrend";
import { SubscribePrompt } from "./components/SubscribePrompt";
import { TrackingPanel } from "./components/TrackingPanel";
import type {
  Plan,
  ScanHistoryPoint,
  ScanReport,
  ShopStatus,
  TrackingReport,
} from "./types";

/** What the page is doing right now. */
type Phase =
  | { kind: "loading" }
  | { kind: "ready" }
  | { kind: "error"; message: string };

/** Which long-running action is in flight, if any. */
type Busy = "none" | "scanning" | "tracking";

export function App() {
  const [phase, setPhase] = useState<Phase>({ kind: "loading" });
  const [busy, setBusy] = useState<Busy>("none");
  const [problem, setProblem] = useState<string | null>(null);

  const [shop, setShop] = useState<ShopStatus | null>(null);
  const [plan, setPlan] = useState<Plan | null>(null);
  const [report, setReport] = useState<ScanReport | null>(null);
  const [history, setHistory] = useState<ScanHistoryPoint[]>([]);
  const [tracking, setTracking] = useState<TrackingReport | null>(null);
  const [trackingAvailable, setTrackingAvailable] = useState(false);

  const load = useCallback(async () => {
    setPhase({ kind: "loading" });

    try {
      // One round trip's worth of latency rather than six.
      const [shopStatus, planDetails, latest, points, availability, lastTracking] =
        await Promise.all([
          api.getShop(),
          api.getPlan(),
          api.getLatestScan(),
          api.getHistory(),
          api.getTrackingAvailability(),
          api.getLatestTracking(),
        ]);

      setShop(shopStatus);
      setPlan(planDetails);
      setReport(latest);
      setHistory(points);
      setTrackingAvailable(availability.available);
      setTracking(lastTracking);
      setPhase({ kind: "ready" });
    } catch (error) {
      setPhase({ kind: "error", message: messageFor(error) });
    }
  }, []);

  /** Runs one of the long actions, keeping the page usable and surfacing failures in place. */
  const run = useCallback(async (kind: Exclude<Busy, "none">, action: () => Promise<void>) => {
    setBusy(kind);
    setProblem(null);

    try {
      await action();
    } catch (error) {
      setProblem(messageFor(error));
    } finally {
      setBusy("none");
    }
  }, []);

  const runScan = useCallback(
    () =>
      run("scanning", async () => {
        setReport(await api.runScan());
        setHistory(await api.getHistory());
      }),
    [run],
  );

  const runTracking = useCallback(
    () => run("tracking", async () => setTracking(await api.runTracking())),
    [run],
  );

  useEffect(() => {
    if (isEmbedded) {
      void load();
    } else {
      setPhase({
        kind: "error",
        message: "Open this app from your Shopify admin. It cannot run on its own.",
      });
    }
  }, [load]);

  const active = shop?.isActive ?? false;

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <h1>AI visibility</h1>
          <p className="muted">
            Whether ChatGPT, Perplexity, Google AI Mode and Claude can read your store — and
            recommend it.
          </p>
        </div>

        {active && (
          <button className="button" onClick={runScan} disabled={busy !== "none"}>
            {busy === "scanning" ? "Scanning…" : "Scan now"}
          </button>
        )}
      </header>

      {phase.kind === "error" && <Notice message={phase.message} onRetry={load} />}
      {phase.kind === "loading" && <p className="muted">Loading…</p>}

      {problem && <Notice message={problem} onRetry={() => setProblem(null)} retryLabel="Dismiss" />}

      {phase.kind === "ready" && (
        <>
          {/* Past reports stay readable to a lapsed shop; only new runs need a plan. */}
          {!active && plan && <SubscribePrompt plan={plan} />}

          {busy === "scanning" && (
            <p className="muted">
              Reading your storefront the way an AI crawler would. This takes a few seconds.
            </p>
          )}

          {!report && active && busy === "none" && <EmptyState onScan={runScan} />}

          {report && (
            <>
              <ScoreCard report={report} />
              <ScoreTrend history={history} />
              <AreaBreakdown areas={report.areas} />
              <FindingsList findings={report.findings} />
            </>
          )}

          {trackingAvailable && (
            <TrackingPanel
              report={tracking}
              onRun={runTracking}
              busy={busy === "tracking"}
              disabled={!active || busy !== "none"}
            />
          )}
        </>
      )}
    </main>
  );
}

/** Shown before the first scan, so the empty dashboard still tells a merchant what to do. */
function EmptyState({ onScan }: { onScan: () => void }) {
  return (
    <section className="card empty">
      <h2>No scan yet</h2>
      <p>
        Run the first scan to see how readable your store is to AI shopping assistants, and
        what to fix.
      </p>
      <button className="button" onClick={onScan}>
        Run the first scan
      </button>
    </section>
  );
}

function Notice({
  message,
  onRetry,
  retryLabel = "Try again",
}: {
  message: string;
  onRetry: () => void;
  retryLabel?: string;
}) {
  return (
    <section className="card notice">
      <p>{message}</p>
      <button className="button button--quiet" onClick={onRetry}>
        {retryLabel}
      </button>
    </section>
  );
}

function messageFor(error: unknown): string {
  return error instanceof Error ? error.message : "Something went wrong.";
}
