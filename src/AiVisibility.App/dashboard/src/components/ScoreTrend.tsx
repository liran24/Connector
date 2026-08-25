import type { ScanHistoryPoint } from "../types";

/**
 * Score over time, as a sparkline.
 *
 * Hand-drawn SVG rather than a charting library: one line on a fixed 0-100 axis does not
 * justify the dependency, and this stays easy to change.
 *
 * Scans that produced no score are gaps in the line, not zeroes — an unreachable store is
 * missing data, and drawing it as a crash would be a lie.
 */
export function ScoreTrend({ history }: { history: ScanHistoryPoint[] }) {
  const scored = history.filter((point): point is ScanHistoryPoint & { score: number } =>
    point.score !== null,
  );

  // One point is not a trend, so the card stays hidden until there is a line to draw.
  if (scored.length < 2) {
    return null;
  }

  const width = 600;
  const height = 120;
  const padding = 8;

  const points = scored.map((point, index) => {
    const x = padding + (index / (scored.length - 1)) * (width - padding * 2);
    const y = padding + (1 - point.score / 100) * (height - padding * 2);
    return `${x.toFixed(1)},${y.toFixed(1)}`;
  });

  const first = scored[0]!.score;
  const last = scored[scored.length - 1]!.score;
  const change = last - first;

  return (
    <section className="card">
      <div className="trend__header">
        <h2>Score over time</h2>
        <span className={`trend__change trend__change--${changeDirection(change)}`}>
          {change > 0 ? `+${change}` : change} since {new Date(scored[0]!.scannedAt).toLocaleDateString()}
        </span>
      </div>

      <svg
        className="trend"
        viewBox={`0 0 ${width} ${height}`}
        preserveAspectRatio="none"
        role="img"
        aria-label={`Score moved from ${first} to ${last} across ${scored.length} scans.`}
      >
        <polyline points={points.join(" ")} fill="none" strokeWidth="2" />
      </svg>
    </section>
  );
}

function changeDirection(change: number): "up" | "down" | "flat" {
  if (change > 0) return "up";
  if (change < 0) return "down";
  return "flat";
}
