import { useCallback, useEffect, useState } from "react";
import { api, isEmbedded } from "./api";
import { AreaBreakdown } from "./components/AreaBreakdown";
import { FindingsList } from "./components/FindingsList";
import { ScoreCard } from "./components/ScoreCard";
import { ScoreTrend } from "./components/ScoreTrend";
import type { ScanHistoryPoint, ScanReport } from "./types";

/** What the page is doing right now. */
type Phase =
  | { kind: "loading" }
  | { kind: "scanning" }
  | { kind: "ready" }
  | { kind: "error"; message: string };

export function App() {
  const [phase, setPhase] = useState<Phase>({ kind: "loading" });
  const [report, setReport] = useState<ScanReport | null>(null);
  const [history, setHistory] = useState<ScanHistoryPoint[]>([]);

  const load = useCallback(async () => {
    try {
      const [latest, points] = await Promise.all([api.getLatestScan(), api.getHistory()]);
      setReport(latest);
      setHistory(points);
      setPhase({ kind: "ready" });
    } catch (error) {
      setPhase({ kind: "error", message: messageFor(error) });
    }
  }, []);

  const runScan = useCallback(async () => {
    setPhase({ kind: "scanning" });

    try {
      setReport(await api.runScan());
      setHistory(await api.getHistory());
      setPhase({ kind: "ready" });
    } catch (error) {
      setPhase({ kind: "error", message: messageFor(error) });
    }
  }, []);

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

        <button
          className="button"
          onClick={runScan}
          disabled={phase.kind === "scanning" || phase.kind === "loading"}
        >
          {phase.kind === "scanning" ? "Scanning…" : "Scan now"}
        </button>
      </header>

      {phase.kind === "error" && <ErrorNotice message={phase.message} onRetry={load} />}

      {phase.kind === "loading" && <p className="muted">Loading…</p>}

      {phase.kind === "scanning" && (
        <p className="muted">
          Reading your storefront the way an AI crawler would. This takes a few seconds.
        </p>
      )}

      {phase.kind === "ready" && !report && <EmptyState onScan={runScan} />}

      {report && (
        <>
          <ScoreCard report={report} />
          <ScoreTrend history={history} />
          <AreaBreakdown areas={report.areas} />
          <FindingsList findings={report.findings} />
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

function ErrorNotice({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <section className="card notice">
      <p>{message}</p>
      <button className="button button--quiet" onClick={onRetry}>
        Try again
      </button>
    </section>
  );
}

function messageFor(error: unknown): string {
  return error instanceof Error ? error.message : "Something went wrong.";
}
