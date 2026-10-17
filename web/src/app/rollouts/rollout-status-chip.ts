import { Component, computed, input } from '@angular/core';
import { RolloutStatus, statusLabel } from '../api/fleet-api';

const chipClass: Record<RolloutStatus, string> = {
  Draft: '',
  Canary: 'live',
  AwaitingPromotion: 'wait',
  Full: 'live',
  Completed: 'ok',
  Halted: 'fail',
  Aborted: '',
};

@Component({
  selector: 'app-rollout-status-chip',
  template: `<span class="chip {{ cls() }}" [attr.data-status]="status()">{{ label() }}</span>`,
})
export class RolloutStatusChip {
  readonly status = input.required<RolloutStatus>();
  protected readonly label = computed(() => statusLabel[this.status()]);
  protected readonly cls = computed(() => chipClass[this.status()]);
}
