import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

// TypeScript mirrors of the API contracts in src/ToolFleet.Api/Contracts.cs.
// Keep them in sync by hand for now; generating them from /openapi/v1.json is a good later step.

export type CalibrationStatus = 'Ok' | 'DueSoon' | 'Overdue';

export interface Location {
  id: string;
  name: string;
  country: string;
}

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

export interface FirmwareRelease {
  id: string;
  model: string;
  version: string;
  notes: string;
  publishedAtUtc: string;
}

export type RolloutStatus =
  | 'Draft'
  | 'Canary'
  | 'AwaitingPromotion'
  | 'Full'
  | 'Completed'
  | 'Halted'
  | 'Aborted';

export type RolloutStage = 'Canary' | 'Full';
export type TargetStatus = 'Pending' | 'Succeeded' | 'Failed';

export interface StageProgress {
  total: number;
  succeeded: number;
  failed: number;
  pending: number;
}

export interface RolloutSummary {
  id: string;
  model: string;
  targetVersion: string;
  status: RolloutStatus;
  createdAtUtc: string;
  canary: StageProgress;
  full: StageProgress;
}

export interface RolloutTarget {
  toolId: string;
  serialNumber: string;
  locationName: string;
  stage: RolloutStage;
  status: TargetStatus;
  previousVersion: string;
  failureReason: string | null;
  reportedAtUtc: string | null;
}

export interface RolloutDetail {
  id: string;
  firmwareReleaseId: string;
  model: string;
  targetVersion: string;
  canaryPercent: number;
  failureThresholdPercent: number;
  status: RolloutStatus;
  haltReason: string | null;
  createdAtUtc: string;
  startedAtUtc: string | null;
  finishedAtUtc: string | null;
  canary: StageProgress;
  full: StageProgress;
  targets: RolloutTarget[];
}

export interface AuditEntry {
  id: number;
  atUtc: string;
  actor: string;
  entityType: string;
  entityId: string;
  action: 'Insert' | 'Update' | 'Delete';
  changes: string; // JSON
}

export const FINISHED_STATUSES: readonly RolloutStatus[] = ['Completed', 'Halted', 'Aborted'];

export const isFinished = (status: RolloutStatus) => FINISHED_STATUSES.includes(status);

/** The stage that is currently flashing tools, if any. */
export const activeStage = (status: RolloutStatus): RolloutStage | null =>
  status === 'Canary' ? 'Canary' : status === 'Full' ? 'Full' : null;

export const statusLabel: Record<RolloutStatus, string> = {
  Draft: 'Draft',
  Canary: 'Canary running',
  AwaitingPromotion: 'Waiting for approval',
  Full: 'Full fleet running',
  Completed: 'Completed',
  Halted: 'Halted',
  Aborted: 'Aborted',
};

/** Pulls the human-readable message out of an API error (problem details or validation problem). */
export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return "Couldn't reach the API. Check that it's running on http://localhost:5080.";
    const body = error.error;
    if (body?.detail) return body.detail;
    if (body?.errors) return Object.values(body.errors as Record<string, string[]>).flat().join(' ');
    if (body?.title) return body.title;
    return `The API returned ${error.status} ${error.statusText}.`;
  }
  return 'Something went wrong. Try again.';
}

/** Write operations. Reads use httpResource in the components. */
@Injectable({ providedIn: 'root' })
export class FleetApi {
  private readonly http = inject(HttpClient);

  publishFirmware(body: { model: string; version: string; notes: string }): Observable<FirmwareRelease> {
    return this.http.post<FirmwareRelease>('/api/firmware', body);
  }

  createRollout(body: { firmwareReleaseId: string; canaryPercent: number; failureThresholdPercent: number }) {
    return this.http.post<RolloutDetail>('/api/rollouts', body);
  }

  getRollout(id: string) {
    return this.http.get<RolloutDetail>(`/api/rollouts/${id}`);
  }

  transition(id: string, action: 'start' | 'promote' | 'abort') {
    return this.http.post<RolloutDetail>(`/api/rollouts/${id}/${action}`, null);
  }

  reportResult(id: string, toolId: string, succeeded: boolean, failureReason: string | null) {
    return this.http.post<RolloutDetail>(`/api/rollouts/${id}/targets/${toolId}/result`, { succeeded, failureReason });
  }

  recordCalibration(toolId: string, calibratedOn: string) {
    return this.http.post<Tool>(`/api/tools/${toolId}/calibrations`, { calibratedOn });
  }
}
