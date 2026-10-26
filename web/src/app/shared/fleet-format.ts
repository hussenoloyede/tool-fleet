import { FirmwareRelease } from '../api/fleet-api';

/** The most recently published build of each model, sorted by model name. */
export function newestPerModel(releases: FirmwareRelease[]): FirmwareRelease[] {
  const newest = new Map<string, FirmwareRelease>();
  for (const r of releases) {
    const current = newest.get(r.model);
    if (!current || r.publishedAtUtc > current.publishedAtUtc) newest.set(r.model, r);
  }
  return [...newest.values()].sort((a, b) => a.model.localeCompare(b.model));
}

/** Whole days from today to an ISO date ("2027-01-18"); negative when it has passed. */
export function daysUntil(isoDate: string, today: Date): number {
  const [y, m, d] = isoDate.split('-').map(Number);
  const target = Date.UTC(y, m - 1, d);
  const start = Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate());
  return Math.round((target - start) / 86_400_000);
}

/** "In 23 months", "Due in 18 days", "Due today", "Overdue by 140d". */
export function dueText(isoDate: string, today: Date): string {
  const days = daysUntil(isoDate, today);
  if (days < 0) return `Overdue by ${-days}d`;
  if (days === 0) return 'Due today';
  if (days <= 30) return `Due in ${days} day${days === 1 ? '' : 's'}`;
  if (days < 60) return `In ${days} days`;
  return `In ${Math.round(days / 30.44)} months`;
}

/** Turns rows into CSV text, quoting every value so commas and quotes inside them are safe. */
export function toCsv(header: string[], rows: (string | number)[][]): string {
  const quote = (v: string | number) => `"${String(v).replaceAll('"', '""')}"`;
  return [header, ...rows].map((r) => r.map(quote).join(',')).join('\r\n');
}
