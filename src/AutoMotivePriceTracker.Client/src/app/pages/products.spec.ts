import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../environments/environment';
import { pricePoint, summary } from '../../testing/fixtures';
import { Products } from './products';

const products = [
  summary({ id: 1, name: 'Penrite Racing Oil', brand: 'Penrite', category: 'engine-oil', retailer: 'Repco' }),
  summary({
    id: 2,
    name: 'Brake Pads Front',
    brand: 'Bendix',
    category: 'brake-pads',
    retailer: 'SupercheapAuto',
    latestPrice: pricePoint({ price: 40, wasPrice: 50, isOnSale: true, discountPercent: 20 }),
  }),
  summary({ id: 3, name: 'Castrol Edge', brand: null, category: 'engine-oil', retailer: 'SupercheapAuto' }),
];

describe('Products page', () => {
  let http: HttpTestingController;

  function render(): HTMLElement {
    const fixture = TestBed.createComponent(Products);
    fixture.detectChanges();
    http.expectOne(`${environment.apiUrl}/api/products`).flush(products);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const names = (el: HTMLElement) =>
    [...el.querySelectorAll('.product-name')].map((n) => n.textContent?.trim());

  function setInput(el: HTMLElement, selector: string, value: string): void {
    const input = el.querySelector<HTMLInputElement | HTMLSelectElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event(input instanceof HTMLSelectElement ? 'change' : 'input'));
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('shows loading, then every product with a count', () => {
    const fixture = TestBed.createComponent(Products);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Loading...');
    http.expectOne(`${environment.apiUrl}/api/products`).flush(products);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('3 products');
    expect(fixture.nativeElement.querySelectorAll('app-product-card').length).toBe(3);
  });

  it('shows an error message when the API fails', () => {
    const fixture = TestBed.createComponent(Products);
    fixture.detectChanges();
    http.expectOne(`${environment.apiUrl}/api/products`).flush('x', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Could not load data');
  });

  it('builds a sorted, distinct category list', () => {
    const el = render();
    const options = [...el.querySelectorAll('select')[0].querySelectorAll('option')].map((o) => o.textContent?.trim());

    expect(options).toEqual(['All Categories', 'brake-pads', 'engine-oil']);
  });

  it('filters by search text on name or brand, case-insensitively', async () => {
    const el = render();

    setInput(el, '.search-input', 'BENDIX');
    TestBed.tick();

    expect(names(el)).toEqual(['Brake Pads Front']);
  });

  it('uses ranked server search results once they answer the typed term', () => {
    vi.useFakeTimers();
    try {
      const el = render();

      setInput(el, '.search-input', 'brak pads');
      TestBed.tick();
      vi.advanceTimersByTime(250);
      const req = http.expectOne(`${environment.apiUrl}/api/products/search?q=brak%20pads`);
      req.flush([3, 2]);
      TestBed.tick();

      // Server order wins and the typo'd term matches, which a substring filter would not.
      expect(names(el)).toEqual(['Castrol Edge', 'Brake Pads Front']);
    } finally {
      vi.useRealTimers();
    }
  });

  it('falls back to substring matching when the search call fails', () => {
    vi.useFakeTimers();
    try {
      const el = render();

      setInput(el, '.search-input', 'bendix');
      TestBed.tick();
      vi.advanceTimersByTime(250);
      http.expectOne(`${environment.apiUrl}/api/products/search?q=bendix`)
        .flush('x', { status: 500, statusText: 'Server Error' });
      TestBed.tick();

      expect(names(el)).toEqual(['Brake Pads Front']);
    } finally {
      vi.useRealTimers();
    }
  });

  it('filters by category and retailer together', () => {
    const el = render();
    const [categorySelect, retailerSelect] = Array.from(el.querySelectorAll('select'));

    categorySelect.value = 'engine-oil';
    categorySelect.dispatchEvent(new Event('change'));
    retailerSelect.value = 'SupercheapAuto';
    retailerSelect.dispatchEvent(new Event('change'));
    TestBed.tick();

    expect(names(el)).toEqual(['Castrol Edge']);
  });

  it('filters to on-sale products only', () => {
    const el = render();
    const checkbox = el.querySelector<HTMLInputElement>('input[type=checkbox]')!;

    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change'));
    TestBed.tick();

    expect(names(el)).toEqual(['Brake Pads Front']);
  });

  it('shows an empty state when nothing matches', () => {
    const el = render();

    setInput(el, '.search-input', 'zzz');
    TestBed.tick();

    expect(el.textContent).toContain('No products match your filters.');
    expect(el.textContent).toContain('0 products');
  });
});
