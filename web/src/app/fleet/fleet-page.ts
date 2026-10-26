import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { CalibrationStatus, errorMessage, FirmwareRelease, FleetApi, Location, Tool } from '../api/fleet-api';
import { daysUntil, dueText, newestPerModel, toCsv } from '../shared/fleet-format';
import { Icon } from '../shared/icon';

@Component({
  selector: 'app-fleet-page',
  imports: [Icon],
  templateUrl: './fleet-page.html',
  styleUrl: './fleet-page.scss',
})
export class FleetPage {
  private readonly api = inject(FleetApi);
  private readonly today = new Date();

  protected readonly model = signal('');
  protected readonly locationId = signal('');
  protected readonly calibration = signal<CalibrationStatus | ''>('');

  /** Every tool, unfiltered: used for the summary cards and the model list. */
  protected readonly allTools = httpResource<Tool[]>(() => '/api/tools');

  /** The filtered list. Filtering happens in the API so the URL is shareable later. */
  protected readonly tools = httpResource<Tool[]>(() => {
    const params: Record<string, string> = {};
    if (this.model()) params['model'] = this.model();
    if (this.locationId()) params['locationId'] = this.locationId();
    if (this.calibration()) params['calibration'] = this.calibration();
    return { url: '/api/tools', params };
  });

  protected readonly locations = httpResource<Location[]>(() => '/api/locations');
  private readonly releases = httpResource<FirmwareRelease[]>(() => '/api/firmware');

  private readonly every = computed(() => (this.allTools.hasValue() ? this.allTools.value() : []));

  protected readonly models = computed(() => [...new Set(this.every().map((t) => t.model))].sort());
  protected readonly totalCount = computed(() => this.every().length);
  protected readonly dueSoon = computed(() => this.every().filter((t) => t.calibrationStatus === 'DueSoon'));
  protected readonly overdue = computed(() => this.every().filter((t) => t.calibrationStatus === 'Overdue'));
  protected readonly dueSoonCount = computed(() => this.dueSoon().length);
  protected readonly overdueCount = computed(() => this.overdue().length);

  protected readonly baseNames = computed(() =>
    (this.locations.hasValue() ? this.locations.value() : []).map((l) => l.name).join(' · '),
  );
  protected readonly country = computed(() => {
    const countries = new Set((this.locations.hasValue() ? this.locations.value() : []).map((l) => l.country));
    return countries.size === 1 ? [...countries][0] : `${countries.size} countries`;
  });

  /** Newest published version of each model. A tool on it gets a cyan version chip. */
  private readonly newest = computed(
    () => new Map(newestPerModel(this.releases.hasValue() ? this.releases.value() : []).map((r) => [r.model, r.version])),
  );

  protected readonly onNewestShare = computed(() => {
    const tools = this.every();
    if (!tools.length || !this.releases.hasValue()) return null;
    return Math.round((100 * tools.filter((t) => this.onNewest(t)).length) / tools.length);
  });

  protected readonly nextDue = computed(() =>
    this.dueSoon().map((t) => t.calibrationDueOn).sort()[0] ?? null,
  );

  protected readonly longestOverdue = computed(() => {
    const days = this.overdue().map((t) => -daysUntil(t.calibrationDueOn, this.today));
    return days.length ? Math.max(...days) : null;
  });

  protected readonly shown = computed(() => (this.tools.hasValue() ? this.tools.value().length : 0));
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

  protected readonly rowClass: Record<CalibrationStatus, string> = {
    Ok: '',
    DueSoon: 'row-warn',
    Overdue: 'row-fail',
  };

  protected onNewest(tool: Tool) {
    return this.newest().get(tool.model) === tool.firmwareVersion;
  }

  protected newestFor(tool: Tool) {
    return this.newest().get(tool.model);
  }

  protected due(tool: Tool) {
    return dueText(tool.calibrationDueOn, this.today);
  }

  /** The Due soon and Overdue cards filter the table to those tools. */
  protected showStatus(status: CalibrationStatus) {
    this.model.set('');
    this.locationId.set('');
    this.calibration.set(status);
  }

  protected clearFilters() {
    this.model.set('');
    this.locationId.set('');
    this.calibration.set('');
  }

  /** Downloads the rows currently in the table. */
  protected exportCsv() {
    const rows = (this.tools.hasValue() ? this.tools.value() : []).map((t) => [
      t.serialNumber,
      t.model,
      t.locationName,
      t.firmwareVersion,
      t.lastCalibratedOn,
      t.calibrationDueOn,
      this.statusLabel[t.calibrationStatus],
    ]);
    const csv = toCsv(
      ['Serial', 'Model', 'Base', 'Firmware', 'Last calibrated', 'Calibration due', 'Status'],
      rows,
    );
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = `toolfleet-${new Date().toISOString().slice(0, 10)}.csv`;
    link.click();
    URL.revokeObjectURL(url);
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
