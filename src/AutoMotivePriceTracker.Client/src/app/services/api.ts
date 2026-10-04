import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { HomeSummary, ProductDetail, ProductSummary, ScrapeStatus } from '../models/models';

@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api`;

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
