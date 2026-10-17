import { AuditEntry } from '../api/fleet-api';
import { describeChanges, humanise } from './audit-format';

const entry = (action: AuditEntry['action'], changes: object): AuditEntry => ({
  id: 1,
  atUtc: '2026-10-17T08:00:00Z',
  actor: 'anonymous',
  entityType: 'Rollout',
  entityId: 'r1',
  action,
  changes: JSON.stringify(changes),
});

describe('audit formatting', () => {
  it('turns property names into words', () => {
    expect(humanise('FirmwareVersion')).toBe('Firmware version');
    expect(humanise('LastCalibratedOn')).toBe('Last calibrated on');
  });

  it('shows updates as from and to, without ids', () => {
    const lines = describeChanges(
      entry('Update', { Status: { from: 'Canary', to: 'Halted' }, RolloutId: { from: 'a', to: 'b' } }),
    );

    expect(lines).toEqual([{ field: 'Status', from: 'Canary', to: 'Halted' }]);
  });

  it('lists inserted values and drops keys and empty fields', () => {
    const lines = describeChanges(
      entry('Insert', { Id: 'x', FirmwareReleaseId: 'y', Model: 'MWD Pulser', HaltReason: null, StartedAtUtc: '2026-10-17T08:00:00.123Z' }),
    );

    expect(lines).toEqual([
      { field: 'Model', to: 'MWD Pulser' },
      { field: 'Started at UTC', to: '2026-10-17 08:00:00' },
    ]);
  });
});
