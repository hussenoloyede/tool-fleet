import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { CalibrationStatus, errorMessage, FleetApi, Location, Tool } from '../api/fleet-api';

@Component({
  selector: 'app-fleet-page',
  templateUrl: './fleet-page.html',
  styleUrl: './fleet-page.scss',
})
export class FleetPage {
  private readonly api = inject(FleetApi);

  protected readonly model = signal('');
  protected readonly locationId = signal('');
  protected readonly calibration = signal<CalibrationStatus | ''>('');

  /** Every tool, unfiltered: used for the summary line and the model list. */
  private readonly allTools = httpResource<Tool[]>(() => '/api/tools');

  /** The filtered list. Filtering happens in the API so the URL is shareable later. */
  protected readonly tools = httpResource<Tool[]>(() => {
    const params: Record<string, string> = {};
    if (this.model()) params['model'] = this.model();
    if (this.locationId()) params['locationId'] = this.locationId();
    if (this.calibration()) params['calibration'] = this.calibration();
    return { url: '/api/tools', params };
  });

  protected readonly locations = httpResource<Location[]>(() => '/api/locations');

  protected readonly models = computed(() =>
    [...new Set((this.allTools.hasValue() ? this.allTools.value() : []).map((t) => t.model))].sort(),
  );

  protected readonly overdueCount = computed(
    () => (this.allTools.hasValue() ? this.allTools.value() : []).filter((t) => t.calibrationStatus === 'Overdue').length,
  );

  protected readonly totalCount = computed(() => (this.allTools.hasValue() ? this.allTools.value().length : 0));

  protected readonly filtered = computed(() => !!(this.model() || this.locationId() || this.calibration()));

  protected readonly savingId = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);

  protected readonly statusLabel: Record<CalibrationStatus, string> = {
    Ok: 'OK',
    DueSoon: 'Due soon',
    Overdue: 'Overdue',
  };

  protected readonly statusClass: Record<CalibrationStatus, string> = {
    Ok: 'ok',
    DueSoon: 'wait',
    Overdue: 'fail',
  };

  protected showOverdue() {
    this.model.set('');
    this.locationId.set('');
    this.calibration.set('Overdue');
  }

  protected clearFilters() {
    this.model.set('');
    this.locationId.set('');
    this.calibration.set('');
  }

  protected markCalibratedToday(tool: Tool) {
    const today = new Date().toISOString().slice(0, 10);
    this.savingId.set(tool.id);
    this.actionError.set(null);
    this.api.recordCalibration(tool.id, today).subscribe({
      next: () => {
        this.savingId.set(null);
        this.tools.reload();
        this.allTools.reload();
      },
      error: (e) => {
        this.savingId.set(null);
        this.actionError.set(`${tool.serialNumber}: ${errorMessage(e)}`);
      },
    });
  }
}
