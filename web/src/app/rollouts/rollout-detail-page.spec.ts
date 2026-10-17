import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RolloutDetail, RolloutStatus, RolloutTarget } from '../api/fleet-api';
import { RolloutDetailPage } from './rollout-detail-page';
import { canarySize } from './rollouts-page';

const target = (serial: string, stage: 'Canary' | 'Full', status: RolloutTarget['status'] = 'Pending'): RolloutTarget => ({
  toolId: `tool-${serial}`,
  serialNumber: serial,
  locationName: 'Labuan',
  stage,
  status,
  previousVersion: '4.1.0',
  failureReason: status === 'Failed' ? 'Checksum mismatch' : null,
  reportedAtUtc: status === 'Pending' ? null : '2026-10-17T08:00:00Z',
});

const rollout = (status: RolloutStatus, targets: RolloutTarget[], haltReason: string | null = null): RolloutDetail => {
  const progress = (stage: 'Canary' | 'Full') => {
    const inStage = targets.filter((t) => t.stage === stage);
    return {
      total: inStage.length,
      succeeded: inStage.filter((t) => t.status === 'Succeeded').length,
      failed: inStage.filter((t) => t.status === 'Failed').length,
      pending: inStage.filter((t) => t.status === 'Pending').length,
    };
  };
  return {
    id: 'r1',
    firmwareReleaseId: 'f1',
    model: 'MWD Pulser',
    targetVersion: '4.2.0',
    canaryPercent: 10,
    failureThresholdPercent: 20,
    status,
    haltReason,
    createdAtUtc: '2026-10-17T07:00:00Z',
    startedAtUtc: status === 'Draft' ? null : '2026-10-17T07:01:00Z',
    finishedAtUtc: null,
    canary: progress('Canary'),
    full: progress('Full'),
    targets,
  };
};

describe('RolloutDetailPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RolloutDetailPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(detail: RolloutDetail) {
    const fixture = TestBed.createComponent(RolloutDetailPage);
    fixture.componentRef.setInput('id', detail.id);
    fixture.detectChanges();
    http.expectOne('/api/rollouts/r1').flush(detail);
    await fixture.whenStable();
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    const buttonTexts = [...page.querySelectorAll('button')].map((b) => b.textContent?.trim());
    return { fixture, page, buttonTexts };
  }

  it('offers Start for a draft and nothing else but Abort', async () => {
    const { buttonTexts } = await render(rollout('Draft', [target('MP-0001', 'Canary'), target('MP-0002', 'Full')]));

    expect(buttonTexts).toEqual(['Start rollout', 'Abort']);
  });

  it('asks for approval after a passed canary and calls promote', async () => {
    const before = rollout('AwaitingPromotion', [target('MP-0001', 'Canary', 'Succeeded'), target('MP-0002', 'Full')]);
    const { fixture, page } = await render(before);

    expect(page.querySelector('.next')?.textContent).toContain('Approve to update the remaining 1 tools');
    const approve = [...page.querySelectorAll('button')].find((b) => b.textContent?.includes('Approve'))!;
    approve.click();

    const req = http.expectOne('/api/rollouts/r1/promote');
    expect(req.request.method).toBe('POST');
    req.flush(rollout('Full', before.targets));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(page.querySelector('app-rollout-status-chip')?.textContent).toContain('Full fleet running');
  });

  it('shows hazard tape and no actions when halted', async () => {
    const { page, buttonTexts } = await render(
      rollout('Halted', [target('MP-0001', 'Canary', 'Failed'), target('MP-0002', 'Full')], '1 of 1 tools failed.'),
    );

    expect(page.querySelector('.hazard')?.textContent).toContain('1 of 1 tools failed.');
    expect(buttonTexts).toEqual([]);
    // Tools the rollout never reached are labelled as such, not as still waiting.
    expect(page.querySelector('.legend')?.textContent).toContain('Not updated');
    expect(page.querySelector('.cell[data-status="Pending"]')?.getAttribute('aria-label')).toContain('not updated');
  });

  it('asks before aborting', async () => {
    const { fixture, page } = await render(rollout('Canary', [target('MP-0001', 'Canary')]));

    [...page.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Abort')!.click();
    fixture.detectChanges();

    expect(page.textContent).toContain('Abort this rollout?');
    http.expectNone('/api/rollouts/r1/abort');
  });

  it('draws one tray cell per tool with its result', async () => {
    const { page } = await render(
      rollout('Full', [
        target('MP-0001', 'Canary', 'Succeeded'),
        target('MP-0002', 'Full', 'Failed'),
        target('MP-0003', 'Full'),
      ]),
    );

    const cells = [...page.querySelectorAll('.cell')].map((c) => c.getAttribute('data-status'));
    expect(cells).toEqual(['Succeeded', 'Failed', 'Pending']);
    expect(page.querySelector('.cell[data-status="Failed"]')?.getAttribute('aria-label')).toContain('Checksum mismatch');
  });
});

describe('canarySize', () => {
  it('matches the domain rule: rounds up, at least one tool', () => {
    expect(canarySize(20, 10)).toBe(2);
    expect(canarySize(25, 10)).toBe(3);
    expect(canarySize(3, 10)).toBe(1);
    expect(canarySize(0, 10)).toBe(0);
  });
});
