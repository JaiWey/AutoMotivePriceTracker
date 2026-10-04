import { Component, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { catchError, debounce, distinctUntilChanged, map, of, switchMap, timer } from 'rxjs';
import { ProductSummary } from '../models/models';
import { Api } from '../services/api';
import { ProductCard } from '../shared/product-card';

@Component({
  selector: 'app-products',
  imports: [FormsModule, ProductCard],
  template: `
    <div class="page-header"><h1>Products</h1></div>

    <div class="filter-bar">
      <input class="search-input" type="text" placeholder="Search name, brand or features..."
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

  private readonly api = inject(Api);

  /** Server-side search result for the term it answered; null while pending, blank or failed. */
  private readonly searchResult = toSignal(
    toObservable(this.search).pipe(
      map((q) => q.trim()),
      distinctUntilChanged(),
      debounce((q) => (q ? timer(250) : of(0))),
      switchMap((term) =>
        term
          ? this.api.searchProducts(term).pipe(
              map((ids) => ({ term, ids })),
              catchError(() => of(null)),
            )
          : of(null),
      ),
    ),
    { initialValue: null },
  );

  protected readonly categories = computed(() =>
    [...new Set(this.all().map((p) => p.category).filter((c): c is string => !!c))].sort(),
  );

  protected readonly filtered = computed(() => {
    const term = this.search().trim().toLowerCase();
    const category = this.category();
    const retailer = this.retailer();
    const onSale = this.onSaleOnly();

    // Ranked server results once they answer the current term; until then (or if the call fails)
    // fall back to a plain substring match so typing never shows a stale or empty list.
    const result = this.searchResult();
    const ranked = term && result?.term.toLowerCase() === term ? result.ids : null;
    let candidates = this.all();
    if (ranked) {
      const byId = new Map(candidates.map((p) => [p.id, p]));
      candidates = ranked.map((id) => byId.get(id)).filter((p): p is ProductSummary => !!p);
    }

    return candidates.filter(
      (p) =>
        (ranked || !term || p.name.toLowerCase().includes(term) || (p.brand ?? '').toLowerCase().includes(term)) &&
        (!category || p.category === category) &&
        (!retailer || p.retailer === retailer) &&
        (!onSale || p.isOnSale),
    );
  });

  constructor() {
    this.api.products().subscribe({
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
