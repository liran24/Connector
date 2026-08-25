import type { ScanReport } from "../types";

/**
 * The headline: one number and one sentence.
 *
 * A null score is shown as "Not available" rather than as zero. Those mean opposite things —
 * zero is a store agents can read and reject, null is a store nobody could read at all — and
 * a merchant who sees a zero for an unreadable store loses trust in every other number here.
 */
export function ScoreCard({ report }: { report: ScanReport }) {
  const scored = report.score !== null;

  return (
    <section className="card score-card">
      <div className={`score score--${bandFor(report.score)}`}>
        {scored ? report.score : "—"}
        {scored && <span className="score__total">/100</span>}
      </div>

      <div className="score-card__text">
        <h2>{scored ? "AI visibility score" : "Score not available"}</h2>
        <p>{report.verdict}</p>
        <p className="muted">
          {report.productsInspected > 0
            ? `${report.productsInspected} products inspected · `
            : ""}
          Scanned {formatWhen(report.scannedAt)}
        </p>
      </div>
    </section>
  );
}

/** Colour band. Kept coarse on purpose: 71 and 74 should not look like different situations. */
function bandFor(score: number | null): "none" | "poor" | "fair" | "good" {
  if (score === null) return "none";
  if (score >= 85) return "good";
  if (score >= 55) return "fair";
  return "poor";
}

export function formatWhen(timestamp: string): string {
  const when = new Date(timestamp);
  const minutesAgo = Math.round((Date.now() - when.getTime()) / 60_000);

  if (minutesAgo < 1) return "just now";
  if (minutesAgo < 60) return `${minutesAgo} minutes ago`;
  if (minutesAgo < 60 * 24) return `${Math.round(minutesAgo / 60)} hours ago`;

  return when.toLocaleDateString();
}
