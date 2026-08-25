import createApp from "@shopify/app-bridge";
import { getSessionToken } from "@shopify/app-bridge/utilities";
import type {
  Plan,
  ScanHistoryPoint,
  ScanReport,
  ShopStatus,
  TrackingReport,
} from "./types";

/**
 * The API client.
 *
 * Every call carries a fresh Shopify session token. The backend takes the shop from that
 * token and never from anything the browser sends, so there is no shop parameter here —
 * one would be an invitation to read another merchant's data.
 */

/** Thrown when the backend answers with an error status. */
export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

/**
 * App Bridge, initialised from the parameters Shopify puts on the iframe URL.
 *
 * Returns null when the page is open outside the Shopify admin — during local development,
 * for instance — so the dashboard can say so plainly instead of failing to render.
 */
function createAppBridge() {
  const params = new URLSearchParams(window.location.search);
  const host = params.get("host");
  const apiKey = import.meta.env["VITE_SHOPIFY_API_KEY"];

  if (!host || !apiKey) {
    return null;
  }

  return createApp({ apiKey, host, forceRedirect: true });
}

const appBridge = createAppBridge();

/** True when the dashboard is running inside the Shopify admin, as it is meant to. */
export const isEmbedded = appBridge !== null;

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  if (!appBridge) {
    throw new ApiError("This dashboard has to be opened from the Shopify admin.", 401);
  }

  // Tokens are short-lived, so one is fetched per request rather than cached.
  const token = await getSessionToken(appBridge);

  const response = await fetch(path, {
    ...init,
    headers: {
      ...init?.headers,
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
    },
  });

  if (response.status === 204) {
    return null as T;
  }

  if (!response.ok) {
    throw new ApiError(await messageFor(response), response.status);
  }

  return (await response.json()) as T;
}

/**
 * Turns an error response into something worth showing a merchant.
 *
 * The backend sends a distinct status and message per failure — a lapsed subscription is not
 * the same problem as a password-protected storefront — so those are surfaced rather than
 * flattened into one generic error.
 */
async function messageFor(response: Response): Promise<string> {
  if (response.status === 401) {
    return "Your session expired. Reload the page.";
  }

  if (response.status === 402) {
    return "This needs an active subscription.";
  }

  try {
    const body = (await response.json()) as { detail?: string; title?: string };
    if (body.detail) return body.detail;
    if (body.title) return body.title;
  } catch {
    // Not a JSON body; fall through to the generic message.
  }

  return `The server returned ${response.status}.`;
}

export const api = {
  getShop: () => request<ShopStatus>("/api/shop"),

  /** The last stored report, or null when this shop has never been scanned. */
  getLatestScan: () => request<ScanReport | null>("/api/scans/latest"),

  getHistory: () => request<ScanHistoryPoint[]>("/api/scans/history"),

  /** Runs a scan now. Takes several seconds — it fetches real pages from the storefront. */
  runScan: () => request<ScanReport>("/api/scans", { method: "POST" }),

  getPlan: () => request<Plan>("/api/billing/plan"),

  /** Creates a charge and returns where to send the merchant to approve it. */
  subscribe: () => request<{ approvalUrl: string }>("/api/billing/subscribe", { method: "POST" }),

  /** Whether this installation has tracking configured at all. */
  getTrackingAvailability: () => request<{ available: boolean }>("/api/tracking/availability"),

  getLatestTracking: () => request<TrackingReport | null>("/api/tracking/latest"),

  /** Runs tracking now. Spends API credit, and takes longer than a scan. */
  runTracking: () => request<TrackingReport>("/api/tracking", { method: "POST" }),
};
