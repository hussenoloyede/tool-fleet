import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./fleet/fleet-page').then((m) => m.FleetPage),
    title: 'Fleet | ToolFleet',
  },
  {
    path: 'firmware',
    loadComponent: () => import('./firmware/firmware-page').then((m) => m.FirmwarePage),
    title: 'Firmware | ToolFleet',
  },
  {
    path: 'rollouts',
    loadComponent: () => import('./rollouts/rollouts-page').then((m) => m.RolloutsPage),
    title: 'Rollouts | ToolFleet',
  },
  {
    path: 'rollouts/:id',
    loadComponent: () => import('./rollouts/rollout-detail-page').then((m) => m.RolloutDetailPage),
    title: 'Rollout | ToolFleet',
  },
  {
    path: 'audit',
    loadComponent: () => import('./audit/audit-page').then((m) => m.AuditPage),
    title: 'Audit log | ToolFleet',
  },
  { path: '**', redirectTo: '' },
];
