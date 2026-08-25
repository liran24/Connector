import type { QuestionOutcome, TrackingReport } from "../types";
import { formatWhen } from "./ScoreCard";

const OUTCOME_LABEL: Record<QuestionOutcome, string> = {
  cited: "Cited",
  "named-only": "Named, not linked",
  absent: "Not mentioned",
  failed: "Failed",
};

/**
 * Whether assistants actually recommend the store.
 *
 * Citations and name-drops are shown as separate numbers, exactly as the backend counts them.
 * An assistant can praise a brand while linking the shopper somewhere else, and merging the
 * two would flatter the merchant with a number they are paying to trust.
 */
export function TrackingPanel({
  report,
  onRun,
  busy,
  disabled,
}: {
  report: TrackingReport | null;
  onRun: () => void;
  busy: boolean;
  disabled: boolean;
}) {
  return (
    <section className="card">
      <div className="trend__header">
        <h2>Are you being recommended?</h2>
        <button className="button button--quiet" onClick={onRun} disabled={busy || disabled}>
          {busy ? "Asking…" : report ? "Run again" : "Run the first check"}
        </button>
      </div>

      {!report && !busy && (
        <p className="muted">
          We ask AI assistants the questions your shoppers ask — without naming your brand —
          and report whether they send people to you.
        </p>
      )}

      {busy && (
        <p className="muted">
          Asking assistants your shoppers&apos; questions. This takes longer than a scan.
        </p>
      )}

      {report && <TrackingResults report={report} />}
    </section>
  );
}

function TrackingResults({ report }: { report: TrackingReport }) {
  return (
    <>
      <p className="tracking__summary">{report.summary}</p>

      <div className="tracking__stats">
        <Stat label="Cited in" value={percent(report.citationRate)} emphasis />
        <Stat label="Mentioned in" value={percent(report.mentionRate)} />
        <Stat
          label="Average position"
          value={report.averageRank !== null ? report.averageRank.toFixed(1) : "—"}
        />
      </div>

      {report.rivals.length > 0 && (
        <>
          <h3 className="tracking__heading">Who is winning these questions</h3>
          <ul className="rivals">
            {report.rivals.map((rival) => (
              <li key={rival.domain} className="rival">
                <span>{rival.domain}</span>
                <span className="muted">
                  cited {rival.timesCited}×
                  {rival.timesAhead > 0 && `, ahead of you ${rival.timesAhead}×`}
                </span>
              </li>
            ))}
          </ul>
        </>
      )}

      <h3 className="tracking__heading">Per question</h3>
      <ul className="questions">
        {report.questions.map((question, index) => (
          <li key={`${question.engine}-${index}`} className="question">
            <span className={`pill pill--${question.outcome}`}>
              {OUTCOME_LABEL[question.outcome]}
              {question.rank !== null && ` #${question.rank}`}
            </span>
            <span className="question__text">{question.question}</span>
          </li>
        ))}
      </ul>

      <p className="muted tracking__caveat">
        This asks the model with web search on, which approximates but does not reproduce what
        a shopper sees in a chat app. Read it as a trend, not a transcript. Last run{" "}
        {formatWhen(report.ranAt)}.
      </p>
    </>
  );
}

function Stat({ label, value, emphasis }: { label: string; value: string; emphasis?: boolean }) {
  return (
    <div className="stat">
      <div className={emphasis ? "stat__value stat__value--strong" : "stat__value"}>{value}</div>
      <div className="stat__label">{label}</div>
    </div>
  );
}

function percent(rate: number): string {
  return `${Math.round(rate * 100)}%`;
}
