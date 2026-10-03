import { CurrencyPipe } from '@angular/common';
import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProductSummary } from '../models/models';
import { RetailerBadge } from './retailer-badge';

@Component({
  selector: 'app-product-card',
  imports: [RouterLink, CurrencyPipe, RetailerBadge],
  styles: `
    .product-card {
      background: var(--card-bg);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      overflow: hidden;
      transition: box-shadow 0.2s;
    }
    .product-card:hover { box-shadow: 0 4px 16px rgba(0, 0, 0, 0.12); }
    .product-card-link { display: block; text-decoration: none; color: inherit; }
    .product-image-wrap { height: 160px; overflow: hidden; display: flex; align-items: center; justify-content: center; background: #f0f0f0; }
    .product-image { width: 100%; height: 100%; object-fit: contain; padding: 0.5rem; }
    .product-image-placeholder { font-size: 3rem; }
    .product-info { padding: 0.75rem; }
    .product-name { font-size: 0.9rem; font-weight: 600; margin: 0.4rem 0; line-height: 1.3; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
  `,
  template: `
    <div class="product-card">
      <a class="product-card-link" [routerLink]="['/product', product().id]">
        <div class="product-image-wrap">
          @if (product().imageUrl) {
            <img class="product-image" [src]="product().imageUrl" [alt]="product().name" loading="lazy" />
          } @else {
            <span class="product-image-placeholder">🔧</span>
          }
        </div>
        <div class="product-info">
          <app-retailer-badge [retailer]="product().retailer" />
          @if (product().category) {
            <span class="category-tag">{{ product().category }}</span>
          }
          <p class="product-name">{{ product().name }}</p>
          @if (product().latestPrice; as p) {
            <div class="price-row">
              <span class="current-price">{{ p.price | currency }}</span>
              @if (p.isOnSale && p.wasPrice) {
                <span class="was-price">was {{ p.wasPrice | currency }}</span>
                <span class="discount-badge">-{{ p.discountPercent }}%</span>
              }
            </div>
          }
        </div>
      </a>
    </div>
  `,
})
export class ProductCard {
  readonly product = input.required<ProductSummary>();
}
