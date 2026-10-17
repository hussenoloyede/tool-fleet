import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { errorMessage, FirmwareRelease, FleetApi, Tool } from '../api/fleet-api';

interface ModelGroup {
  model: string;
  toolCount: number;
  releases: { release: FirmwareRelease; onVersion: number }[];
}

@Component({
  selector: 'app-firmware-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './firmware-page.html',
  styleUrl: './firmware-page.scss',
})
export class FirmwarePage {
  private readonly api = inject(FleetApi);

  protected readonly releases = httpResource<FirmwareRelease[]>(() => '/api/firmware');
  protected readonly tools = httpResource<Tool[]>(() => '/api/tools');

  protected readonly models = computed(() =>
    [...new Set((this.tools.hasValue() ? this.tools.value() : []).map((t) => t.model))].sort(),
  );

  /** Releases grouped by model, newest first, with how many tools already run each version. */
  protected readonly groups = computed<ModelGroup[]>(() => {
    if (!this.releases.hasValue() || !this.tools.hasValue()) return [];
    const tools = this.tools.value();
    const byModel = new Map<string, FirmwareRelease[]>();
    for (const r of this.releases.value()) {
      byModel.set(r.model, [...(byModel.get(r.model) ?? []), r]);
    }
    return [...byModel.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([model, releases]) => {
        const modelTools = tools.filter((t) => t.model === model);
        return {
          model,
          toolCount: modelTools.length,
          releases: releases
            .sort((a, b) => b.publishedAtUtc.localeCompare(a.publishedAtUtc))
            .map((release) => ({
              release,
              onVersion: modelTools.filter((t) => t.firmwareVersion === release.version).length,
            })),
        };
      });
  });

  protected readonly form = inject(FormBuilder).nonNullable.group({
    model: ['', Validators.required],
    version: ['', [Validators.required, Validators.pattern(/^\d+\.\d+\.\d+$/)]],
    notes: [''],
  });

  protected readonly saving = signal(false);
  protected readonly publishError = signal<string | null>(null);
  protected readonly justPublished = signal<FirmwareRelease | null>(null);

  protected publish() {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.publishError.set(null);
    this.api.publishFirmware(this.form.getRawValue()).subscribe({
      next: (release) => {
        this.saving.set(false);
        this.justPublished.set(release);
        this.form.reset({ model: release.model, version: '', notes: '' });
        this.releases.reload();
      },
      error: (e) => {
        this.saving.set(false);
        this.publishError.set(errorMessage(e));
      },
    });
  }
}
