import { Component, computed, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  activeStage,
  errorMessage,
  FleetApi,
  isFinished,
  RolloutDetail,
  RolloutStage,
  RolloutTarget,
} from '../api/fleet-api';
import { Icon } from '../shared/icon';
import { RolloutStatusChip } from './rollout-status-chip';

const POLL_MS = 2000;
const SIMULATOR_STEP_MS = 450;
const FAILURE_REASONS = ['Checksum mismatch', 'Flash write timeout', 'Battery below 20%', 'Bootloader rejected image'];

const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

@Component({
  selector: 'app-rollout-detail-page',
  imports: [RouterLink, RolloutStatusChip, Icon],
  templateUrl: './rollout-detail-page.html',
  styleUrl: './rollout-detail-page.scss',
})
export class RolloutDetailPage {
  private readonly api = inject(FleetApi);

  /** Route parameter :id (bound by withComponentInputBinding). */
  readonly id = input.required<string>();

  protected readonly rollout = signal<RolloutDetail | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly busy = signal<'start' | 'promote' | 'abort' | null>(null);
  protected readonly confirmingAbort = signal(false);

  protected readonly failRate = signal(10);
  protected readonly simulating = signal(false);
  private stopSimulator = false;

  protected readonly stage = computed(() => {
    const r = this.rollout();
    return r ? activeStage(r.status) : null;
  });

  protected readonly finished = computed(() => {
    const r = this.rollout();
    return r ? isFinished(r.status) : false;
  });

  /** One row per stage for the core tray. */
  protected readonly trayRows = computed(() => {
    const r = this.rollout();
    if (!r) return [];
    return [
      { name: 'Canary', stage: 'Canary' as RolloutStage, targets: this.targetsIn('Canary'), progress: r.canary },
      { name: 'Rest of fleet', stage: 'Full' as RolloutStage, targets: this.targetsIn('Full'), progress: r.full },
    ];
  });
  protected readonly failedTargets = computed(() => (this.rollout()?.targets ?? []).filter((t) => t.status === 'Failed'));

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => this.load(id));
    });

    // Refresh while the rollout is live, so results sent by tools/simulator.cs appear too.
    const timer = setInterval(() => {
      const r = this.rollout();
      if (r && !isFinished(r.status) && !this.busy() && !this.simulating()) this.load(r.id);
    }, POLL_MS);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(timer);
      this.stopSimulator = true;
    });
  }

  private targetsIn(stage: RolloutStage): RolloutTarget[] {
    return (this.rollout()?.targets ?? []).filter((t) => t.stage === stage);
  }

  private load(id: string) {
    this.api.getRollout(id).subscribe({
      next: (r) => {
        this.rollout.set(r);
        this.loadError.set(null);
      },
      error: (e) => this.loadError.set(errorMessage(e)),
    });
  }

  protected act(action: 'start' | 'promote' | 'abort') {
    const r = this.rollout();
    if (!r) return;
    if (action === 'abort') this.stopSimulator = true;
    this.busy.set(action);
    this.actionError.set(null);
    this.api.transition(r.id, action).subscribe({
      next: (updated) => {
        this.rollout.set(updated);
        this.busy.set(null);
        this.confirmingAbort.set(false);
      },
      error: (e) => {
        this.actionError.set(errorMessage(e));
        this.busy.set(null);
        this.confirmingAbort.set(false);
      },
    });
  }

  /**
   * Plays the part of the field tools for the running stage: reports a result for each
   * pending tool, one at a time, through the same endpoint a real tool would call.
   */
  protected async runSimulator() {
    const r = this.rollout();
    const stage = this.stage();
    if (!r || !stage || this.simulating()) return;

    this.simulating.set(true);
    this.stopSimulator = false;
    this.actionError.set(null);
    try {
      for (const target of r.targets.filter((t) => t.stage === stage && t.status === 'Pending')) {
        await wait(SIMULATOR_STEP_MS);
        if (this.stopSimulator) break;
        const succeeded = Math.random() * 100 >= this.failRate();
        const reason = succeeded ? null : FAILURE_REASONS[Math.floor(Math.random() * FAILURE_REASONS.length)];
        const updated = await firstValueFrom(this.api.reportResult(r.id, target.toolId, succeeded, reason));
        this.rollout.set(updated);
        if (activeStage(updated.status) !== stage) break; // halted, waiting for approval, or completed
      }
    } catch (e) {
      if (!this.stopSimulator) this.actionError.set(errorMessage(e));
    } finally {
      this.simulating.set(false);
    }
  }

  protected stop() {
    this.stopSimulator = true;
  }

  protected cellLabel(t: RolloutTarget) {
    const result = t.status === 'Pending' ? (this.finished() ? 'not updated' : 'waiting') : t.status === 'Succeeded' ? 'updated' : `failed: ${t.failureReason}`;
    return `${t.serialNumber} at ${t.locationName}, ${result}`;
  }

  protected shortSerial(serial: string) {
    return serial.split('-').pop() ?? serial;
  }

  protected time(iso: string | null) {
    return iso ? iso.slice(0, 19).replace('T', ' ') : '';
  }
}
