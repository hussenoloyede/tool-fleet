import { AuditEntry } from '../api/fleet-api';

/** "FirmwareVersion" -> "Firmware version", "LastCalibratedOn" -> "Last calibrated on". */
export function humanise(name: string): string {
  const words = name.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase();
  const sentence = words.charAt(0).toUpperCase() + words.slice(1);
  return sentence.replace(/\butc\b/g, 'UTC');
}

export const entityLabel: Record<string, string> = {
  Rollout: 'Rollout',
  RolloutTarget: 'Rollout result',
  Tool: 'Tool',
  FirmwareRelease: 'Firmware build',
  FieldLocation: 'Base',
};

export interface ChangeLine {
  field: string;
  from?: string;
  to: string;
}

const show = (value: unknown): string => {
  if (value === null || value === undefined || value === '') return 'empty';
  if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(value)) return value.slice(0, 19).replace('T', ' ');
  return String(value);
};

// Ids and foreign keys are noise in a human-readable log.
const isKey = (field: string) => field === 'Id' || field.endsWith('Id');

/** Turns the stored JSON into readable lines: "Status: Canary → Halted". */
export function describeChanges(entry: AuditEntry): ChangeLine[] {
  let parsed: Record<string, unknown>;
  try {
    parsed = JSON.parse(entry.changes) as Record<string, unknown>;
  } catch {
    return [{ field: 'Changes', to: entry.changes }];
  }

  return Object.entries(parsed)
    .filter(([field]) => !isKey(field))
    .map(([field, value]) => {
      if (entry.action === 'Update' && value && typeof value === 'object' && 'to' in value) {
        const v = value as { from: unknown; to: unknown };
        return { field: humanise(field), from: show(v.from), to: show(v.to) };
      }
      return { field: humanise(field), to: show(value) };
    })
    .filter((line) => !(line.from === undefined && line.to === 'empty'));
}
