import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HomeSummary } from '../models/models';
import { Api } from '../services/api';
import { ProductCard } from '../shared/product-card';

@Component({
  selector: 'app-home',
  imports: [RouterLink, ProductCard],
  template: `
    <div class="home-hero">
      <h1>🔧 AutoPrice NZ</h1>
      <p>Track automotive part prices across New Zealand retailers.</p>
      <a class="btn-primary" routerLink="/products">Browse Products</a>
    </div>

    @if (error()) {
      <p class="empty-state">Could not load data from the API.</p>
    } @else if (summary(); as s) {
      <div class="section">
        <h2>Current Deals</h2>
        @if (s.deals.length) {
          <div class="product-grid">
            @for (p of s.deals; track p.id) {
              <app-product-card [product]="p" />
            }
          </div>
        } @else {
          <p class="empty-state">No deals found yet. Visit Admin to run a scrape.</p>
        }
      </div>

      <div class="section">
        <h2>Recently Updated</h2>
        <div class="product-grid">
          @for (p of s.recent; track p.id) {
            <app-product-card [product]="p" />
          }
        </div>
      </div>

      <div class="stats-bar">
        <div class="stat"><span class="stat-value">{{ s.totalProducts }}</span><span class="stat-label">Products tracked</span></div>
        <div class="stat"><span class="stat-value">{{ s.countByRetailer['Repco'] ?? 0 }}</span><span class="stat-label">Repco</span></div>
        <div class="stat"><span class="stat-value">{{ s.countByRetailer['SupercheapAuto'] ?? 0 }}</span><span class="stat-label">Supercheap Auto</span></div>
      </div>
    } @else {
      <p>Loading...</p>
    }
  `,
})
export class Home {
  protected readonly summary = signal<HomeSummary | null>(null);
  protected readonly error = signal(false);

  constructor() {
    inject(Api).home().subscribe({
      next: (s) => this.summary.set(s),
      error: () => this.error.set(true),
    });
  }
}
