import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { ScrapeStatus } from '../models/models';
import { Api } from '../services/api';

const POLL_MS = 3000;

@Component({
  selector: 'app-admin',
  imports: [DatePipe],
  template: `
    <div class="admin-page">
      <div class="page-header"><h1>Admin</h1></div>
      <div class="admin-section">
        <h2>Price Scraping</h2>
        <p>Scrapes all configured retailers and records any price changes. This can take several minutes.</p>
        <button class="btn-primary" (click)="start()" [disabled]="running()">
          {{ running() ? 'Scraping...' : 'Run Scrape Now' }}
        </button>

        @if (requestError()) {
          <div class="status-message error">{{ requestError() }}</div>
        } @else {
          @switch (status()?.state) {
            @case ('Running') {
              <div class="status-message info">Scrape running since {{ status()!.startedAt | date: 'mediumTime' }}…</div>
            }
            @case ('Succeeded') {
              <div class="status-message success">Scrape completed at {{ status()!.finishedAt | date: 'medium' }}.</div>
            }
            @case ('Failed') {
              <div class="status-message error">Scrape failed: {{ status()!.error }}</div>
            }
          }
        }
      </div>
    </div>
  `,
})
export class Admin {
  private readonly api = inject(Api);
  private timer: ReturnType<typeof setTimeout> | undefined;

  protected readonly status = signal<ScrapeStatus | null>(null);
  protected readonly requestError = signal<string | null>(null);
  protected readonly running = computed(() => this.status()?.state === 'Running');

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
    // Pick up a run already in progress (manual or scheduled).
    this.refresh();
  }

  protected start(): void {
    this.requestError.set(null);
    this.api.startScrape().subscribe({
      next: (s) => {
        this.status.set(s);
        this.poll();
      },
      // 409 = a scrape is already running; just follow it.
      error: (e) => {
        if (e.status === 409) {
          this.status.set(e.error);
          this.poll();
        } else {
          this.requestError.set('Could not start the scrape.');
        }
      },
    });
  }

  private refresh(): void {
    this.api.scrapeStatus().subscribe({
      next: (s) => {
        this.status.set(s);
        if (s.state === 'Running') this.poll();
      },
      error: () => this.requestError.set('Could not reach the API.'),
    });
  }

  private poll(): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.refresh(), POLL_MS);
  }
}
