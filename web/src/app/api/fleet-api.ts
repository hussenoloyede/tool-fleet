// TypeScript mirrors of the API contracts in src/ToolFleet.Api/Contracts.cs.
// Keep them in sync by hand for now; generating them from /openapi/v1.json is a good later step.

export type CalibrationStatus = 'Ok' | 'DueSoon' | 'Overdue';

export interface Tool {
  id: string;
  serialNumber: string;
  model: string;
  locationId: string;
  locationName: string;
  firmwareVersion: string;
  lastCalibratedOn: string; // ISO date, e.g. "2026-10-11"
  calibrationDueOn: string;
  calibrationStatus: CalibrationStatus;
}

export type RolloutStatus =
  | 'Draft'
  | 'Canary'
  | 'AwaitingPromotion'
  | 'Full'
  | 'Completed'
  | 'Halted'
  | 'Aborted';

export interface StageProgress {
  total: number;
  succeeded: number;
  failed: number;
  pending: number;
}
