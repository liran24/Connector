import type { Finding, Severity } from "../types";

const SEVERITY_LABEL: Record<Severity, string> = {
  critical: "Critical",
  important: "Important",
  minor: "Worth doing",
};

/**
 * What to fix, worst first.
 *
 * This is the part a merchant acts on, so each entry leads with the problem, then why it
 * costs them something, then the fix. The backend already orders by severity.
 */
export function FindingsList({ findings }: { findings: Finding[] }) {
  if (findings.length === 0) {
    return (
      <section className="card">
        <h2>Nothing to fix</h2>
        <p>This store reads cleanly to AI shopping agents.</p>
      </section>
    );
  }

  return (
    <section className="card">
      <h2>
        What to fix <span className="muted">({findings.length})</span>
      </h2>

      <ul className="findings">
        {findings.map((finding) => (
          <li key={finding.code} className="finding">
            <div className="finding__header">
              <span className={`badge badge--${finding.severity}`}>
                {SEVERITY_LABEL[finding.severity]}
              </span>
              <h3>{finding.title}</h3>
            </div>

            <p>{finding.detail}</p>
            <p className="finding__fix">
              <strong>Fix:</strong> {finding.fix}
            </p>

            {finding.url && (
              <a className="finding__link" href={finding.url} target="_blank" rel="noreferrer">
                Seen at {finding.url}
              </a>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}
