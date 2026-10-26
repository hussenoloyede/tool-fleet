import { daysUntil, dueText, toCsv } from './fleet-format';

describe('fleet formatting', () => {
  const today = new Date('2026-10-27T03:00:00Z');

  it('counts whole days to a date', () => {
    expect(daysUntil('2026-10-27', today)).toBe(0);
    expect(daysUntil('2026-11-14', today)).toBe(18);
    expect(daysUntil('2026-06-09', today)).toBe(-140);
  });

  it('describes when calibration is due', () => {
    expect(dueText('2026-06-09', today)).toBe('Overdue by 140d');
    expect(dueText('2026-10-27', today)).toBe('Due today');
    expect(dueText('2026-10-28', today)).toBe('Due in 1 day');
    expect(dueText('2026-11-14', today)).toBe('Due in 18 days');
    expect(dueText('2028-09-27', today)).toBe('In 23 months');
  });

  it('quotes CSV values', () => {
    expect(toCsv(['a', 'b'], [['x, y', 'say "hi"']])).toBe('"a","b"\r\n"x, y","say ""hi"""');
  });
});
