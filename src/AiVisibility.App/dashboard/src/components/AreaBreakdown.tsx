import type { Area } from "../types";

/**
 * The four scored areas, each as a bar.
 *
 * An area that could not be verified shows its reason instead of a bar. Drawing an empty bar
 * would read as "zero", which is the false-all-clear problem in reverse.
 */
export function AreaBreakdown({ areas }: { areas: Area[] }) {
  return (
    <section className="card">
      <h2>Where the score comes from</h2>

      <ul className="areas">
        {areas.map((area) => (
          <li key={area.key} className="area">
            <div className="area__header">
              <span className="area__name">{area.name}</span>
              <span className="area__score">
                {area.score !== null ? `${area.score}/100` : "Not checked"}
              </span>
            </div>

            {area.score !== null ? (
              <div className="bar" role="img" aria-label={`${area.score} out of 100`}>
                <div className="bar__fill" style={{ width: `${area.score}%` }} />
              </div>
            ) : (
              <p className="muted area__reason">{area.notCheckedReason}</p>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}
