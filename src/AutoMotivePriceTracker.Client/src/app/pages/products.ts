import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ProductSummary } from '../models/models';
import { Api } from '../services/api';
import { ProductCard } from '../shared/product-card';

@Component({
  selector: 'app-products',
  imports: [FormsModule, ProductCard],
  template: `
    <div class="page-header"><h1>Products</h1></div>

    <div class="filter-bar">
      <input class="search-input" type="text" placeholder="Search name or brand..."
             [ngModel]="search()" (ngModelChange)="search.set($event)" />
      <select class="filter-select" [ngModel]="category()" (ngModelChange)="category.set($event)">
        <option value="">All Categories</option>
        @for (c of categories(); track c) {
          <option [value]="c">{{ c }}</option>
        }
      </select>
      <select class="filter-select" [ngModel]="retailer()" (ngModelChange)="retailer.set($event)">
        <option value="">All Retailers</option>
        <option value="Repco">Repco</option>
        <option value="SupercheapAuto">SupercheapAuto</option>
      </select>
      <label class="toggle-label">
        <input type="checkbox" [ngModel]="onSaleOnly()" (ngModelChange)="onSaleOnly.set($event)" /> On Sale Only
      </label>
    </div>

    @if (error()) {
      <p class="empty-state">Could not load data from the API.</p>
    } @else if (loading()) {
      <p>Loading...</p>
    } @else {
      <p class="result-count">{{ filtered().length }} products</p>
      @if (filtered().length) {
        <div class="product-grid">
          @for (p of filtered(); track p.id) {
            <app-product-card [product]="p" />
          }
        </div>
      } @else {
        <p class="empty-state">No products match your filters.</p>
      }
    }
  `,
})
export class Products {
  private readonly all = signal<ProductSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal(false);

  protected readonly search = signal('');
  protected readonly category = signal('');
  protected readonly retailer = signal('');
  protected readonly onSaleOnly = signal(false);

  protected readonly categories = computed(() =>
    [...new Set(this.all().map((p) => p.category).filter((c): c is string => !!c))].sort(),
  );

  protected readonly filtered = computed(() => {
    const term = this.search().trim().toLowerCase();
    const category = this.category();
    const retailer = this.retailer();
    const onSale = this.onSaleOnly();
    return this.all().filter(
      (p) =>
        (!term || p.name.toLowerCase().includes(term) || (p.brand ?? '').toLowerCase().includes(term)) &&
        (!category || p.category === category) &&
        (!retailer || p.retailer === retailer) &&
        (!onSale || p.isOnSale),
    );
  });

  constructor() {
    inject(Api).products().subscribe({
      next: (products) => {
        this.all.set(products);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }
}
