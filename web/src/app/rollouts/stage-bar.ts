import { Component, computed, input } from '@angular/core';
import { StageProgress } from '../api/fleet-api';

/** Compact proportional bar: succeeded, failed, pending. Used in the rollouts list. */
@Component({
  selector: 'app-stage-bar',
  template: `
    @if (progress().total > 0) {
      <span class="bar" role="img" [attr.aria-label]="label()">
        <span class="seg ok" [style.flex-grow]="progress().succeeded"></span>
        <span class="seg fail" [style.flex-grow]="progress().failed"></span>
        <span class="seg pending" [style.flex-grow]="progress().pending"></span>
      </span>
      <span class="nums">{{ progress().succeeded + progress().failed }}/{{ progress().total }}</span>
    } @else {
      <span class="muted">None</span>
    }
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 0.5rem;
      white-space: nowrap;
    }
    .bar {
      display: flex;
      gap: 2px;
      width: 6rem;
      height: 0.35rem;
      border-radius: 999px;
      overflow: hidden;
      background: var(--pending-fill);
    }
    .seg.ok { background: var(--ok-fill); }
    .seg.fail { background: var(--fail-fill); }
    .seg.pending { background: var(--pending-fill); }
    .nums { font-family: var(--mono); font-size: 0.78rem; color: var(--muted); }
  `,
})
export class StageBar {
  readonly progress = input.required<StageProgress>();

  protected readonly label = computed(() => {
    const p = this.progress();
    return `${p.succeeded} succeeded, ${p.failed} failed, ${p.pending} pending of ${p.total}`;
  });
}
