import { PricePoint, ProductDetail, ProductSummary } from '../app/models/models';

export function pricePoint(overrides: Partial<PricePoint> = {}): PricePoint {
  return {
    date: '2026-01-01T00:00:00Z',
    price: 10,
    wasPrice: null,
    isOnSale: false,
    discountPercent: null,
    ...overrides,
  };
}

export function summary(overrides: Partial<ProductSummary> = {}): ProductSummary {
  const latestPrice = overrides.latestPrice === undefined ? pricePoint() : overrides.latestPrice;
  return {
    id: 1,
    name: 'Engine Oil 5L',
    brand: null,
    category: 'engine-oil',
    imageUrl: null,
    retailer: 'Repco',
    lastChecked: '2026-01-01T00:00:00Z',
    isOnSale: latestPrice?.isOnSale ?? false,
    ...overrides,
    latestPrice,
  };
}

export function detail(overrides: Partial<ProductDetail> = {}): ProductDetail {
  return {
    id: 1,
    name: 'Engine Oil 5L',
    brand: 'Penrite',
    category: 'engine-oil',
    imageUrl: null,
    sourceUrl: 'https://example.test/p/1',
    retailer: 'Repco',
    lastChecked: '2026-01-02T00:00:00Z',
    latestPrice: pricePoint(),
    priceHistory: [pricePoint()],
    features: [],
    ...overrides,
  };
}
