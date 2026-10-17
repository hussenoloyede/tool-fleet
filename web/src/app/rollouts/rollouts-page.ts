import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { startWith } from 'rxjs';
import { errorMessage, FirmwareRelease, FleetApi, isFinished, RolloutSummary, Tool } from '../api/fleet-api';
import { RolloutStatusChip } from './rollout-status-chip';
import { StageBar } from './stage-bar';

/** Same rule as Rollout.CanarySize in the domain: percentage rounded up, at least one tool. */
export const canarySize = (targets: number, percent: number) =>
  targets === 0 ? 0 : Math.max(1, Math.ceil((targets * percent) / 100));

@Component({
  selector: 'app-rollouts-page',
  imports: [ReactiveFormsModule, RouterLink, StageBar, RolloutStatusChip],
  templateUrl: './rollouts-page.html',
  styleUrl: './rollouts-page.scss',
})
export class RolloutsPage implements OnInit {
  private readonly api = inject(FleetApi);
  private readonly router = inject(Router);

  /** ?release=<id> from the Firmware page's "Roll out" button. */
  readonly release = input<string>();

  protected readonly rollouts = httpResource<RolloutSummary[]>(() => '/api/rollouts');
  protected readonly releases = httpResource<FirmwareRelease[]>(() => '/api/firmware');
  protected readonly tools = httpResource<Tool[]>(() => '/api/tools');

  protected readonly form = inject(FormBuilder).nonNullable.group({
    firmwareReleaseId: ['', Validators.required],
    canaryPercent: [10, [Validators.required, Validators.min(1), Validators.max(50)]],
    failureThresholdPercent: [20, [Validators.required, Validators.min(0), Validators.max(100)]],
  });

  private readonly formValue = toSignal(this.form.valueChanges.pipe(startWith(this.form.getRawValue())), {
    requireSync: true,
  });

  protected readonly activeModels = computed(
    () =>
      new Set(
        (this.rollouts.hasValue() ? this.rollouts.value() : []).filter((r) => !isFinished(r.status)).map((r) => r.model),
      ),
  );

  protected readonly selectedRelease = computed(() => {
    const id = this.formValue().firmwareReleaseId;
    return (this.releases.hasValue() ? this.releases.value() : []).find((r) => r.id === id) ?? null;
  });

  /** What the rollout would cover, so nobody is surprised after pressing Create. */
  protected readonly preview = computed(() => {
    const release = this.selectedRelease();
    if (!release || !this.tools.hasValue()) return null;
    const eligible = this.tools.value().filter((t) => t.model === release.model && t.firmwareVersion !== release.version);
    const canary = canarySize(eligible.length, this.formValue().canaryPercent ?? 10);
    return {
      release,
      eligible: eligible.length,
      canary,
      full: eligible.length - canary,
      blocked: this.activeModels().has(release.model),
    };
  });

  protected readonly saving = signal(false);
  protected readonly createError = signal<string | null>(null);

  ngOnInit() {
    // Pre-select the release passed in from the Firmware page.
    const id = this.release();
    if (id) this.form.controls.firmwareReleaseId.setValue(id);
  }

  protected releaseLabel(r: FirmwareRelease) {
    return `${r.model} ${r.version}`;
  }

  protected create() {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.createError.set(null);
    this.api.createRollout(this.form.getRawValue()).subscribe({
      next: (rollout) => this.router.navigate(['/rollouts', rollout.id]),
      error: (e) => {
        this.saving.set(false);
        this.createError.set(errorMessage(e));
      },
    });
  }
}
