import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./fleet/fleet-page').then((m) => m.FleetPage), title: 'Fleet · ToolFleet' },
  // Week 2: rollouts list, rollout detail, audit log.
  { path: '**', redirectTo: '' },
];
