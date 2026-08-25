import { useState } from "react";
import { api } from "../api";
import type { Plan } from "../types";

/**
 * Asks the merchant to start their subscription.
 *
 * Shown when the shop is not active. Approving happens on Shopify's own screen, so this
 * redirects the whole window rather than the iframe — a payment approval inside an embedded
 * frame is exactly the pattern browsers and merchants are right to distrust.
 */
export function SubscribePrompt({ plan }: { plan: Plan }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function start() {
    setBusy(true);
    setError(null);

    try {
      const { approvalUrl } = await api.subscribe();
      window.top!.location.href = approvalUrl;
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Could not start the subscription.");
      setBusy(false);
    }
  }

  const price = new Intl.NumberFormat(undefined, {
    style: "currency",
    currency: plan.currency,
  }).format(plan.monthlyPrice);

  return (
    <section className="card subscribe">
      <h2>Start your {plan.trialDays}-day free trial</h2>

      <p>
        Scanning and tracking make live requests to your storefront and to AI assistants, so
        they need an active plan. {price} per month after the trial
        {plan.trialDays > 0 ? ", cancel any time" : ""}.
      </p>

      {plan.isTest && (
        <p className="muted">
          This installation is in test mode — you will see a real approval screen but will not
          be charged.
        </p>
      )}

      {error && <p className="subscribe__error">{error}</p>}

      <button className="button" onClick={start} disabled={busy}>
        {busy ? "Opening Shopify…" : `Start free trial`}
      </button>
    </section>
  );
}
