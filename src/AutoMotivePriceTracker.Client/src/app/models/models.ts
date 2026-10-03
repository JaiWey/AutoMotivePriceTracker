export type Retailer = 'Repco' | 'SupercheapAuto';

export interface PricePoint {
  date: string;
  price: number;
  wasPrice: number | null;
  isOnSale: boolean;
  discountPercent: number | null;
}

export interface ProductSummary {
  id: number;
  name: string;
  brand: string | null;
  category: string | null;
  imageUrl: string | null;
  retailer: Retailer;
  lastChecked: string;
  latestPrice: PricePoint | null;
  isOnSale: boolean;
}

export interface ProductDetail {
  id: number;
  name: string;
  brand: string | null;
  category: string | null;
  imageUrl: string | null;
  sourceUrl: string;
  retailer: Retailer;
  lastChecked: string;
  latestPrice: PricePoint | null;
  priceHistory: PricePoint[];
  features: string[];
}

export interface HomeSummary {
  deals: ProductSummary[];
  recent: ProductSummary[];
  totalProducts: number;
  countByRetailer: Partial<Record<Retailer, number>>;
}

export type ScrapeState = 'Idle' | 'Running' | 'Succeeded' | 'Failed';

export interface ScrapeStatus {
  state: ScrapeState;
  startedAt: string | null;
  finishedAt: string | null;
  error: string | null;
}
