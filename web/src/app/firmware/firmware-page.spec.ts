import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { FirmwareRelease, formatBytes, Tool } from '../api/fleet-api';
import { FirmwarePage } from './firmware-page';

const sha = 'a'.repeat(64);

const release = (version: string, publishedAtUtc: string, withFile = true): FirmwareRelease => ({
  id: `rel-${version}`,
  model: 'MWD Pulser',
  version,
  notes: '',
  publishedAtUtc,
  file: withFile ? { fileName: `mp-${version}.bin`, sizeBytes: 2_500_000, sha256: sha } : null,
});

const tool: Tool = {
  id: 't1',
  serialNumber: 'MP-0001',
  model: 'MWD Pulser',
  locationId: 'loc-1',
  locationName: 'Labuan',
  firmwareVersion: '4.1.0',
  lastCalibratedOn: '2026-06-01',
  calibrationDueOn: '2026-11-28',
  calibrationStatus: 'Ok',
};

describe('formatBytes', () => {
  it('picks a readable unit', () => {
    expect(formatBytes(512)).toBe('512 bytes');
    expect(formatBytes(3_500)).toBe('3.4 KB');
    expect(formatBytes(50 * 1024 * 1024)).toBe('50 MB');
  });
});

describe('FirmwarePage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FirmwarePage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  const render = async (releases: FirmwareRelease[]) => {
    const fixture = TestBed.createComponent(FirmwarePage);
    fixture.detectChanges();
    http.expectOne('/api/firmware').flush(releases);
    http.expectOne('/api/tools').flush([tool]);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  };

  it('shows each file with its size and fingerprint, and "No file" for baseline builds', async () => {
    const fixture = await render([release('4.2.0', '2026-10-20T00:00:00Z'), release('4.1.0', '2026-04-01T00:00:00Z', false)]);
    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr');

    expect(rows[0].textContent).toContain('mp-4.2.0.bin');
    expect(rows[0].textContent).toContain('2.4 MB');
    expect(rows[0].querySelector('abbr')?.getAttribute('title')).toBe(sha);
    expect(rows[1].textContent).toContain('No file');
    expect(rows[1].querySelector('button')).toBeNull();
  });

  it('downloads through a short-lived link from the API', async () => {
    const open = vi
      .spyOn(FirmwarePage.prototype as unknown as { openUrl: (url: string) => void }, 'openUrl')
      .mockImplementation(() => {});
    const fixture = await render([release('4.2.0', '2026-10-20T00:00:00Z')]);

    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('tbody button')!.click();
    http.expectOne('/api/firmware/rel-4.2.0/download').flush({
      url: 'https://files.example/mp-4.2.0.bin?sig=x',
      expiresAtUtc: '2026-10-20T00:15:00Z',
      fileName: 'mp-4.2.0.bin',
      sizeBytes: 2_500_000,
      sha256: sha,
    });

    expect(open).toHaveBeenCalledWith('https://files.example/mp-4.2.0.bin?sig=x');
  });

  it('will not publish without a file', async () => {
    const fixture = await render([]);
    const page = fixture.nativeElement as HTMLElement;

    page.querySelector('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(page.textContent).toContain('Choose the firmware file.');
    http.expectNone((r) => r.method === 'POST');
  });
});
