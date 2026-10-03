import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { pricePoint, summary } from '../../testing/fixtures';
import { PriceChart } from './price-chart';
import { ProductCard } from './product-card';
import { RetailerBadge } from './retailer-badge';

describe('RetailerBadge', () => {
  it.each([
    ['Repco', 'badge-repco', 'Repco'],
    ['SupercheapAuto', 'badge-supercheap', 'SupercheapAuto'],
  ] as const)('renders %s', (retailer, cssClass, label) => {
    const fixture = TestBed.createComponent(RetailerBadge);
    fixture.componentRef.setInput('retailer', retailer);
    fixture.detectChanges();

    const badge = fixture.nativeElement.querySelector('.badge');
    expect(badge.classList).toContain(cssClass);
    expect(badge.textContent.trim()).toBe(label);
  });
});

describe('ProductCard', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideRouter([])] }));

  function render(product = summary()) {
    const fixture = TestBed.createComponent(ProductCard);
    fixture.componentRef.setInput('product', product);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('links to the product page and shows name, category and price', () => {
    const el = render(summary({ id: 5, name: 'Oil Filter' }));

    expect(el.querySelector('a')!.getAttribute('href')).toBe('/product/5');
    expect(el.textContent).toContain('Oil Filter');
    expect(el.textContent).toContain('engine-oil');
    expect(el.querySelector('.current-price')!.textContent).toContain('$10.00');
    expect(el.querySelector('.was-price')).toBeNull();
  });

  it('shows a placeholder when there is no image and the image when there is', () => {
    expect(render().querySelector('img')).toBeNull();
    expect(render().textContent).toContain('🔧');
    expect(render(summary({ imageUrl: 'http://img/a.jpg' })).querySelector('img')!.getAttribute('src')).toBe('http://img/a.jpg');
  });

  it('shows was-price and discount when on sale', () => {
    const el = render(summary({ latestPrice: pricePoint({ price: 75, wasPrice: 100, isOnSale: true, discountPercent: 25 }) }));

    expect(el.querySelector('.was-price')!.textContent).toContain('$100.00');
    expect(el.querySelector('.discount-badge')!.textContent).toContain('-25%');
  });

  it('omits the price row when the product has no price', () => {
    expect(render(summary({ latestPrice: null })).querySelector('.price-row')).toBeNull();
  });
});

describe('PriceChart', () => {
  function create(records: ReturnType<typeof pricePoint>[]) {
    const fixture = TestBed.createComponent(PriceChart);
    fixture.componentRef.setInput('records', records);
    fixture.detectChanges();
    return fixture;
  }

  it.each([[0], [1]])('shows "not enough data" for %i record(s)', (count) => {
    const fixture = create(Array.from({ length: count }, () => pricePoint()));

    expect(fixture.nativeElement.textContent).toContain('Not enough data to display a chart.');
    expect(fixture.nativeElement.querySelector('canvas')).toBeNull();
  });

  it('orders chart data by date and uses prices as values', () => {
    // Inspect the computed chart data. jsdom has no canvas, so Chart.js's getContext warning in the output is expected.
    const fixture = TestBed.createComponent(PriceChart);
    fixture.componentRef.setInput('records', [
      pricePoint({ date: '2026-03-01T00:00:00Z', price: 30 }),
      pricePoint({ date: '2026-01-01T00:00:00Z', price: 10 }),
      pricePoint({ date: '2026-02-01T00:00:00Z', price: 20 }),
    ]);

    expect(fixture.componentInstance.data().datasets[0].data).toEqual([10, 20, 30]);
    expect(fixture.componentInstance.data().labels).toHaveLength(3);
  });
});
