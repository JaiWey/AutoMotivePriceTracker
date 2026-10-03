import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { ScrapeStatus } from '../models/models';
import { Admin } from './admin';

const url = `${environment.apiUrl}/api/admin/scrape`;
const status = (state: ScrapeStatus['state'], extra: Partial<ScrapeStatus> = {}): ScrapeStatus => ({
  state,
  startedAt: '2026-01-01T00:00:00Z',
  finishedAt: null,
  error: null,
  ...extra,
});

describe('Admin page', () => {
  let http: HttpTestingController;

  function create() {
    const fixture = TestBed.createComponent(Admin);
    fixture.detectChanges();
    return fixture;
  }
  const button = (f: { nativeElement: HTMLElement }) => f.nativeElement.querySelector('button')!;
  const text = (f: { nativeElement: HTMLElement }) => f.nativeElement.textContent ?? '';

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  it('is idle when no scrape has run', () => {
    const fixture = create();
    http.expectOne(`${url}/status`).flush(status('Idle'));
    fixture.detectChanges();

    expect(button(fixture).disabled).toBe(false);
    expect(button(fixture).textContent).toContain('Run Scrape Now');
  });

  it('starts a scrape, disables the button, polls and reports success', () => {
    const fixture = create();
    http.expectOne(`${url}/status`).flush(status('Idle'));
    fixture.detectChanges();

    button(fixture).click();
    http.expectOne({ method: 'POST', url }).flush(status('Running'), { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();
    expect(button(fixture).disabled).toBe(true);
    expect(button(fixture).textContent).toContain('Scraping...');

    vi.advanceTimersByTime(3000);
    http.expectOne(`${url}/status`).flush(status('Running'));
    vi.advanceTimersByTime(3000);
    http.expectOne(`${url}/status`).flush(status('Succeeded', { finishedAt: '2026-01-01T00:05:00Z' }));
    fixture.detectChanges();

    expect(button(fixture).disabled).toBe(false);
    expect(text(fixture)).toContain('Scrape completed');
    vi.advanceTimersByTime(10000); // polling has stopped; afterEach verify() would fail on a request
  });

  it('follows an already-running scrape when the API answers 409', () => {
    const fixture = create();
    http.expectOne(`${url}/status`).flush(status('Idle'));
    button(fixture).click();

    http.expectOne({ method: 'POST', url }).flush(status('Running'), { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(button(fixture).disabled).toBe(true);
    expect(text(fixture)).not.toContain('Could not start');
    vi.advanceTimersByTime(3000);
    http.expectOne(`${url}/status`).flush(status('Succeeded'));
  });

  it('resumes polling when a scrape is already running on load', () => {
    const fixture = create();
    http.expectOne(`${url}/status`).flush(status('Running'));
    fixture.detectChanges();
    expect(button(fixture).disabled).toBe(true);

    vi.advanceTimersByTime(3000);
    http.expectOne(`${url}/status`).flush(status('Failed', { error: 'boom' }));
    fixture.detectChanges();

    expect(text(fixture)).toContain('Scrape failed: boom');
  });

  it('shows an error if the scrape cannot be started', () => {
    const fixture = create();
    http.expectOne(`${url}/status`).flush(status('Idle'));
    button(fixture).click();

    http.expectOne({ method: 'POST', url }).flush('x', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(text(fixture)).toContain('Could not start the scrape.');
  });

  it('stops polling when destroyed', () => {
    const fixture = create();
    http.expectOne(`${url}/status`).flush(status('Running'));

    fixture.destroy();
    vi.advanceTimersByTime(10000);

    http.expectNone(`${url}/status`);
  });
});
