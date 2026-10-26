import { httpResource } from '@angular/common/http';
import { Component, computed, DestroyRef, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

type ApiState = 'online' | 'waking' | 'offline';

@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  /** The free Azure tier sleeps when idle, so the header says whether the API is awake. */
  private readonly health = httpResource.text(() => '/health');

  protected readonly apiState = computed<ApiState>(() => {
    if (this.health.hasValue()) return this.health.value() === 'Healthy' ? 'online' : 'offline';
    return this.health.error() ? 'offline' : 'waking';
  });

  protected readonly apiLabel: Record<ApiState, string> = {
    online: 'API online',
    waking: 'Waking API…',
    offline: 'API offline',
  };

  constructor() {
    const timer = setInterval(() => this.health.reload(), 60_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }
}
