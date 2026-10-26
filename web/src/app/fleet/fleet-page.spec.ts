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

  const flushTools = (tools: Tool[]) => {
    // The page loads the full list (for the summary) and the filtered list (for the table).
    for (const req of http.match((r) => r.url === '/api/tools')) req.flush(tools);
    http.expectOne('/api/locations').flush([{ id: 'loc-1', name: 'Labuan', country: 'Malaysia' }]);
    http.expectOne('/api/firmware').flush([]);
  };

  it('lists tools and counts the overdue ones', async () => {
    const fixture = TestBed.createComponent(FleetPage);
    fixture.detectChanges();

    flushTools([tool({ serialNumber: 'MP-0001' }), tool({ serialNumber: 'MP-0002', calibrationStatus: 'Overdue' })]);
    await fixture.whenStable();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelectorAll('tbody tr').length).toBe(2);
    expect(page.querySelector('[data-stat="Overdue"] .stat-value')?.textContent?.trim()).toBe('1');
    expect(page.querySelector('[data-stat="DueSoon"] .stat-value')?.textContent?.trim()).toBe('0');
    expect(page.querySelector('.chip[data-status="Overdue"]')?.textContent?.trim()).toBe('Overdue');
  });

  it('offers "Calibrated today" only for tools that are due or overdue', async () => {
    const fixture = TestBed.createComponent(FleetPage);
    fixture.detectChanges();

    flushTools([
      tool({ serialNumber: 'MP-0001', calibrationStatus: 'Ok' }),
      tool({ serialNumber: 'MP-0002', calibrationStatus: 'DueSoon' }),
    ]);
    await fixture.whenStable();

    const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody button');
    expect(buttons.length).toBe(1);
    expect(buttons[0].closest('tr')?.textContent).toContain('MP-0002');
  });

  it('shows a message when the API is unreachable', async () => {
    const fixture = TestBed.createComponent(FleetPage);
    fixture.detectChanges();

    for (const req of http.match((r) => r.url === '/api/tools')) {
      req.flush('down', { status: 502, statusText: 'Bad Gateway' });
    }
    http.expectOne('/api/locations').flush([]);
    http.expectOne('/api/firmware').flush([]);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')).toBeTruthy();
  });
});
