import { HttpClient } from '@angular/common/http';
import { Injectable, REQUEST, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { HomeSummary, ProductDetail, ProductSummary, ScrapeStatus } from '../models/models';

/**
 * Origin the API is reached at. A configured `apiUrl` wins (local dev, e2e). When it is empty the API is
 * served from the same origin as the app (production behind one CDN): the browser then uses relative URLs,
 * and server-side rendering resolves them against the incoming request's origin.
 */
export function resolveApiOrigin(configured: string, request: { url: string } | null): string {
  if (configured) return configured;
  return request ? new URL(request.url).origin : '';
}

@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);
  private readonly base = `${resolveApiOrigin(environment.apiUrl, inject(REQUEST, { optional: true }))}/api`;

  home(): Observable<HomeSummary> {
    return this.http.get<HomeSummary>(`${this.base}/home`);
  }

  products(): Observable<ProductSummary[]> {
    return this.http.get<ProductSummary[]>(`${this.base}/products`);
  }

  /** Ids of products matching the free-text query (typo-tolerant, incl. features), best match first. */
  searchProducts(query: string): Observable<number[]> {
    return this.http.get<number[]>(`${this.base}/products/search`, { params: { q: query } });
  }

  product(id: number): Observable<ProductDetail> {
    return this.http.get<ProductDetail>(`${this.base}/products/${id}`);
  }

  startScrape(): Observable<ScrapeStatus> {
    return this.http.post<ScrapeStatus>(`${this.base}/admin/scrape`, null);
  }

  scrapeStatus(): Observable<ScrapeStatus> {
    return this.http.get<ScrapeStatus>(`${this.base}/admin/scrape/status`);
  }
}
