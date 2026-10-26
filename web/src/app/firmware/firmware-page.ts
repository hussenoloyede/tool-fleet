import { httpResource } from '@angular/common/http';
import { Component, computed, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  errorMessage,
  FirmwareRelease,
  FleetApi,
  formatBytes,
  isFinished,
  MAX_FIRMWARE_BYTES,
  RolloutSummary,
  Tool,
} from '../api/fleet-api';
import { Icon } from '../shared/icon';

interface ModelGroup {
  model: string;
  toolCount: number;
  /** A rollout for this model is running, so "Roll out" would be refused. */
  rolloutActive: boolean;
  /** Share of this model's tools on its newest build. */
  coverage: { percent: number; onNewest: number; version: string };
  releases: { release: FirmwareRelease; onVersion: number }[];
}

@Component({
  selector: 'app-firmware-page',
  imports: [ReactiveFormsModule, RouterLink, Icon],
  templateUrl: './firmware-page.html',
  styleUrl: './firmware-page.scss',
})
export class FirmwarePage {
  private readonly api = inject(FleetApi);

  protected readonly releases = httpResource<FirmwareRelease[]>(() => '/api/firmware');
  protected readonly tools = httpResource<Tool[]>(() => '/api/tools');
  private readonly rollouts = httpResource<RolloutSummary[]>(() => '/api/rollouts');

  private readonly activeModels = computed(
    () =>
      new Set(
        (this.rollouts.hasValue() ? this.rollouts.value() : []).filter((r) => !isFinished(r.status)).map((r) => r.model),
      ),
  );

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
        const rows = releases
          .sort((a, b) => b.publishedAtUtc.localeCompare(a.publishedAtUtc))
          .map((release) => ({
            release,
            onVersion: modelTools.filter((t) => t.firmwareVersion === release.version).length,
          }));
        const newest = rows[0];
        return {
          model,
          toolCount: modelTools.length,
          rolloutActive: this.activeModels().has(model),
          coverage: {
            percent: modelTools.length ? Math.round((100 * newest.onVersion) / modelTools.length) : 0,
            onNewest: newest.onVersion,
            version: newest.release.version,
          },
          releases: rows,
        };
      });
  });

  protected readonly form = inject(FormBuilder).nonNullable.group({
    model: ['', Validators.required],
    version: ['', [Validators.required, Validators.pattern(/^\d+\.\d+\.\d+$/)]],
    notes: [''],
  });

  protected readonly formatBytes = formatBytes;
  protected readonly maxSize = formatBytes(MAX_FIRMWARE_BYTES);

  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');
  private readonly modelSelect = viewChild<ElementRef<HTMLSelectElement>>('modelSelect');

  /** The "Publish build" button at the top jumps to the form. */
  protected focusPublish() {
    const select = this.modelSelect()?.nativeElement;
    select?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    select?.focus({ preventScroll: true });
  }

  protected percent(part: number, whole: number) {
    return whole ? Math.round((100 * part) / whole) : 0;
  }
  protected readonly file = signal<File | null>(null);
  protected readonly fileError = signal<string | null>(null);

  protected readonly saving = signal(false);
  protected readonly publishError = signal<string | null>(null);
  protected readonly justPublished = signal<FirmwareRelease | null>(null);

  /** Release id whose download link is being fetched. */
  protected readonly downloading = signal<string | null>(null);
  protected readonly downloadError = signal<string | null>(null);

  protected onFileChosen(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.file.set(file);
    this.fileError.set(this.checkFile(file));
  }

  private checkFile(file: File | null): string | null {
    if (!file) return 'Choose the firmware file.';
    if (file.size === 0) return 'That file is empty.';
    if (file.size > MAX_FIRMWARE_BYTES) return `That file is ${formatBytes(file.size)}. The limit is ${this.maxSize}.`;
    return null;
  }

  protected publish() {
    const file = this.file();
    const fileProblem = this.checkFile(file);
    if (this.form.invalid || fileProblem || !file) {
      this.form.markAllAsTouched();
      this.fileError.set(fileProblem);
      return;
    }
    this.saving.set(true);
    this.publishError.set(null);
    this.api.publishFirmware({ ...this.form.getRawValue(), file }).subscribe({
      next: (release) => {
        this.saving.set(false);
        this.justPublished.set(release);
        this.form.reset({ model: release.model, version: '', notes: '' });
        this.file.set(null);
        const input = this.fileInput()?.nativeElement;
        if (input) input.value = '';
        this.releases.reload();
      },
      error: (e) => {
        this.saving.set(false);
        this.publishError.set(errorMessage(e));
      },
    });
  }

  /** Asks the API for a 15-minute link, then lets the browser download from it. */
  protected download(release: FirmwareRelease) {
    this.downloading.set(release.id);
    this.downloadError.set(null);
    this.api.firmwareDownload(release.id).subscribe({
      next: (link) => {
        this.downloading.set(null);
        this.openUrl(link.url);
      },
      error: (e) => {
        this.downloading.set(null);
        this.downloadError.set(errorMessage(e));
      },
    });
  }

  protected openUrl(url: string) {
    window.location.assign(url);
  }
}
