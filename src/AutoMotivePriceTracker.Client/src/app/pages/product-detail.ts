import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { ProductDetail } from '../models/models';
import { Api } from '../services/api';
import { PriceChart } from '../shared/price-chart';
import { RetailerBadge } from '../shared/retailer-badge';

@Component({
  selector: 'app-product-detail',
  imports: [CurrencyPipe, DatePipe, PriceChart, RetailerBadge],
  template: `
    @if (loading()) {
      <p>Loading...</p>
    } @else if (product(); as p) {
      <div class="product-detail">
        <div class="product-detail-header">
          @if (p.imageUrl) {
            <img class="detail-image" [src]="p.imageUrl" [alt]="p.name" />
          }
          <div class="detail-meta">
            <app-retailer-badge [retailer]="p.retailer" />
            @if (p.category) {
              <span class="category-tag">{{ p.category }}</span>
            }
            <h1>{{ p.name }}</h1>
            @if (p.brand) {
              <p class="brand">Brand: {{ p.brand }}</p>
            }
            @if (p.latestPrice; as latest) {
              <div class="price-row large">
                <span class="current-price">{{ latest.price | currency }}</span>
                @if (latest.isOnSale && latest.wasPrice) {
                  <span class="was-price">was {{ latest.wasPrice | currency }}</span>
                  <span class="discount-badge">-{{ latest.discountPercent }}%</span>
                }
              </div>
            }
            <p class="last-checked">Last checked: {{ p.lastChecked | date: 'short' }}</p>
            <a class="btn-secondary" [href]="p.sourceUrl" target="_blank" rel="noopener">View on {{ p.retailer }}</a>
          </div>
        </div>

        @if (p.features.length) {
          <div class="features-section">
            <h2>Features</h2>
            <ul>
              @for (f of p.features; track $index) {
                <li>{{ f }}</li>
              }
            </ul>
          </div>
        }

        <div class="price-history-section">
          <h2>Price History</h2>
          <app-price-chart [records]="p.priceHistory" />
        </div>

        <div class="price-history-table">
          <h2>Price Records</h2>
          <table>
            <thead><tr><th>Date</th><th>Price</th><th>Was</th><th>Discount</th></tr></thead>
            <tbody>
              @for (r of newestFirst(); track r.date) {
                <tr>
                  <td>{{ r.date | date: 'short' }}</td>
                  <td>{{ r.price | currency }}</td>
                  <td>{{ r.wasPrice != null ? (r.wasPrice | currency) : '-' }}</td>
                  <td>{{ r.discountPercent != null ? r.discountPercent + '%' : '-' }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </div>
    } @else {
      <p>Product not found.</p>
    }
  `,
})
export class ProductDetailPage {
  private readonly api = inject(Api);

  /** Bound from the :id route param (withComponentInputBinding). */
  readonly id = input.required<string>();

  protected readonly product = signal<ProductDetail | null>(null);
  protected readonly loading = signal(true);

  protected readonly newestFirst = computed(() =>
    [...(this.product()?.priceHistory ?? [])].sort(
      (a, b) => new Date(b.date).getTime() - new Date(a.date).getTime(),
    ),
  );

  constructor() {
    effect((onCleanup) => {
      this.loading.set(true);
      const sub = this.api.product(Number(this.id())).subscribe({
        next: (p) => {
          this.product.set(p);
          this.loading.set(false);
        },
        error: () => {
          this.product.set(null);
          this.loading.set(false);
        },
      });
      onCleanup(() => sub.unsubscribe());
    });
  }
}
