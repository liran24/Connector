/**
 * The shapes the backend returns.
 *
 * These mirror the C# records in `Scanning/ScanReport.cs` and
 * `Endpoints/DashboardApiEndpoints.cs`. Change one side and you must change the other —
 * there is no code generation here, deliberately, because the surface is small enough that
 * a generator would cost more than it saves.
 */

export type Severity = "critical" | "important" | "minor";

export type ScanStatus = "complete" | "partial" | "unreachable";

/** One thing to fix, with the reason it matters and what to do about it. */
export interface Finding {
  code: string;
  severity: Severity;
  title: string;
  detail: string;
  fix: string;
  /** The page or file it was seen on, when there is one. */
  url: string | null;
}

/** One scored area. A null score means the area could not be verified. */
export interface Area {
  key: string;
  name: string;
  score: number | null;
  /** Why the area could not be checked; null when it was. */
  notCheckedReason: string | null;
}

/** A complete scan report. */
export interface ScanReport {
  /** 0-100, or null when the storefront could not be read at all. */
  score: number | null;
  status: ScanStatus;
  verdict: string;
  productsInspected: number;
  scannedAt: string;
  areas: Area[];
  findings: Finding[];
}

export interface ShopStatus {
  domain: string;
  subscriptionStatus: string;
  isActive: boolean;
  installedAt: string;
}

export interface ScanHistoryPoint {
  scannedAt: string;
  score: number | null;
}
