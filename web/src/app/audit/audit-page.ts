import { httpResource } from '@angular/common/http';
import { Component, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuditEntry } from '../api/fleet-api';
import { describeChanges, entityLabel } from './audit-format';

@Component({
  selector: 'app-audit-page',
  imports: [RouterLink],
  templateUrl: './audit-page.html',
  styleUrl: './audit-page.scss',
})
export class AuditPage {
  protected readonly entityType = signal('');
  protected readonly take = signal(100);

  protected readonly entries = httpResource<AuditEntry[]>(() => {
    const params: Record<string, string | number> = { take: this.take() };
    if (this.entityType()) params['entityType'] = this.entityType();
    return { url: '/api/audit', params };
  });

  protected readonly rows = computed(() =>
    (this.entries.hasValue() ? this.entries.value() : []).map((entry) => ({ entry, lines: describeChanges(entry) })),
  );

  protected readonly entityTypes = Object.entries(entityLabel);
  protected readonly entityLabel = entityLabel;

  protected readonly actionLabel: Record<AuditEntry['action'], string> = {
    Insert: 'Created',
    Update: 'Changed',
    Delete: 'Deleted',
  };

  protected time(iso: string) {
    return iso.slice(0, 19).replace('T', ' ');
  }
}
