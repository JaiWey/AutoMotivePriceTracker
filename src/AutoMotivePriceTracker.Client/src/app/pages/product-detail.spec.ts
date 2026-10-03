import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { detail, pricePoint } from '../../testing/fixtures';
import { ProductDetailPage } from './product-detail';

describe('ProductDetailPage', () => {
  let http: HttpTestingController;

  function create(id: string) {
    const fixture = TestBed.createComponent(ProductDetailPage);
    fixture.componentRef.setInput('id', id);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the product for the route id and shows its details', () => {
    const fixture = create('7');
    expect(fixture.nativeElement.textContent).toContain('Loading...');

    http.expectOne(`${environment.apiUrl}/api/products/7`).flush(
      detail({
        latestPrice: pricePoint({ price: 75, wasPrice: 100, isOnSale: true, discountPercent: 25 }),
        priceHistory: [pricePoint({ price: 75, wasPrice: 100, isOnSale: true, discountPercent: 25 })],
      }),
    );
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Engine Oil 5L');
    expect(text).toContain('Brand: Penrite');
    expect(text).toContain('$75.00');
    expect(text).toContain('was $100.00');
    expect(text).toContain('-25%');
    expect(text).toContain('Not enough data to display a chart.');
    expect(fixture.nativeElement.querySelector('a.btn-secondary').getAttribute('href')).toBe('https://example.test/p/1');
  });

  it('shows the product features, and no Features section when there are none', () => {
    const fixture = create('1');
    http.expectOne(`${environment.apiUrl}/api/products/1`).flush(detail({ features: ['Hyspec', '5L'] }));
    fixture.detectChanges();

    const items = [...fixture.nativeElement.querySelectorAll('.features-section li')].map((li) => (li as HTMLElement).textContent?.trim());
    expect(items).toEqual(['Hyspec', '5L']);

    const empty = create('2');
    http.expectOne(`${environment.apiUrl}/api/products/2`).flush(detail({ features: [] }));
    empty.detectChanges();
    expect(empty.nativeElement.querySelector('.features-section')).toBeNull();
  });

  it('lists price records newest first with "-" for missing values', () => {
    const fixture = create('1');
    http.expectOne(`${environment.apiUrl}/api/products/1`).flush(
      detail({
        priceHistory: [
          pricePoint({ date: '2026-01-01T12:00:00Z', price: 10 }),
          pricePoint({ date: '2026-03-01T12:00:00Z', price: 30, wasPrice: 40, discountPercent: 25, isOnSale: true }),
          pricePoint({ date: '2026-02-01T12:00:00Z', price: 20 }),
        ],
      }),
    );
    fixture.detectChanges();

    const rows = [...fixture.nativeElement.querySelectorAll('tbody tr')].map((tr) =>
      [...(tr as HTMLElement).querySelectorAll('td')].map((td) => td.textContent?.trim()),
    );
    expect(rows.map((r) => r[1])).toEqual(['$30.00', '$20.00', '$10.00']);
    expect(rows[0].slice(2)).toEqual(['$40.00', '25%']);
    expect(rows[1].slice(2)).toEqual(['-', '-']);
  });

  it('shows "Product not found." when the API returns 404', () => {
    const fixture = create('999');
    http.expectOne(`${environment.apiUrl}/api/products/999`).flush(null, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Product not found.');
  });

  it('reloads when the id changes', () => {
    const fixture = create('1');
    http.expectOne(`${environment.apiUrl}/api/products/1`).flush(detail());
    fixture.detectChanges();

    fixture.componentRef.setInput('id', '2');
    fixture.detectChanges();

    http.expectOne(`${environment.apiUrl}/api/products/2`).flush(detail({ id: 2, name: 'Second' }));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Second');
  });
});
