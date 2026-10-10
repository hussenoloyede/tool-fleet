import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Tool } from '../api/fleet-api';
import { FleetPage } from './fleet-page';

const tool = (overrides: Partial<Tool>): Tool => ({
  id: crypto.randomUUID(),
  serialNumber: 'MP-0001',
  model: 'MWD Pulser',
  locationId: 'loc-1',
  locationName: 'Labuan',
  firmwareVersion: '4.1.0',
  lastCalibratedOn: '2026-06-01',
  calibrationDueOn: '2026-11-28',
  calibrationStatus: 'Ok',
  ...overrides,
});

describe('FleetPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FleetPage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists tools and counts the overdue ones', async () => {
    const fixture = TestBed.createComponent(FleetPage);
    fixture.detectChanges();

    http.expectOne('/api/tools').flush([
      tool({ serialNumber: 'MP-0001' }),
      tool({ serialNumber: 'MP-0002', calibrationStatus: 'Overdue' }),
    ]);
    await fixture.whenStable();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelectorAll('tbody tr').length).toBe(2);
    expect(page.querySelector('.overdue-count')?.textContent).toContain('1 overdue');
    expect(page.querySelector('[data-status="Overdue"]')?.textContent?.trim()).toBe('Overdue');
  });

  it('shows a message when the API is unreachable', async () => {
    const fixture = TestBed.createComponent(FleetPage);
    fixture.detectChanges();

    http.expectOne('/api/tools').flush('down', { status: 502, statusText: 'Bad Gateway' });
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')).toBeTruthy();
  });
});
