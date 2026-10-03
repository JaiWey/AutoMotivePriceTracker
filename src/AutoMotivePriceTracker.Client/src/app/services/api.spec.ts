import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { Api } from './api';

describe('Api', () => {
  let api: Api;
  let http: HttpTestingController;
  const base = `${environment.apiUrl}/api`;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(Api);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('requests the home summary', () => {
    api.home().subscribe();
    expect(http.expectOne(`${base}/home`).request.method).toBe('GET');
  });

  it('requests all products', () => {
    api.products().subscribe();
    expect(http.expectOne(`${base}/products`).request.method).toBe('GET');
  });

  it('requests one product by id', () => {
    api.product(42).subscribe();
    expect(http.expectOne(`${base}/products/42`).request.method).toBe('GET');
  });

  it('starts a scrape with POST', () => {
    api.startScrape().subscribe();
    expect(http.expectOne(`${base}/admin/scrape`).request.method).toBe('POST');
  });

  it('polls scrape status', () => {
    api.scrapeStatus().subscribe();
    expect(http.expectOne(`${base}/admin/scrape/status`).request.method).toBe('GET');
  });
});
