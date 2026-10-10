import { httpResource } from '@angular/common/http';
import { Component, computed } from '@angular/core';
import { CalibrationStatus, Tool } from '../api/fleet-api';

@Component({
  selector: 'app-fleet-page',
  templateUrl: './fleet-page.html',
  styleUrl: './fleet-page.scss',
})
export class FleetPage {
  protected readonly tools = httpResource<Tool[]>(() => '/api/tools');

  protected readonly overdueCount = computed(
    () => (this.tools.hasValue() ? this.tools.value() : []).filter((t) => t.calibrationStatus === 'Overdue').length,
  );

  protected readonly statusLabel: Record<CalibrationStatus, string> = {
    Ok: 'OK',
    DueSoon: 'Due soon',
    Overdue: 'Overdue',
  };
}
