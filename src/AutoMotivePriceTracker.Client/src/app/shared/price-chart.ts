import { Component, computed, input } from '@angular/core';
import { ChartConfiguration } from 'chart.js';
import { BaseChartDirective } from 'ng2-charts';
import { PricePoint } from '../models/models';

const dayMonth = new Intl.DateTimeFormat('en-NZ', { day: '2-digit', month: 'short' });
const fullDate = new Intl.DateTimeFormat('en-NZ', { day: '2-digit', month: 'short', year: 'numeric' });

@Component({
  selector: 'app-price-chart',
  imports: [BaseChartDirective],
  template: `
    @if (records().length < 2) {
      <p class="empty-state">Not enough data to display a chart.</p>
    } @else {
      <div class="chart-wrap">
        <canvas baseChart type="line" [data]="data()" [options]="options"></canvas>
      </div>
    }
  `,
  styles: `.chart-wrap { position: relative; height: 260px; }`,
})
export class PriceChart {
  readonly records = input.required<PricePoint[]>();

  private readonly sorted = computed(() =>
    [...this.records()].sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime()),
  );

  readonly data = computed<ChartConfiguration<'line'>['data']>(() => ({
    labels: this.sorted().map((r) => dayMonth.format(new Date(r.date))),
    datasets: [
      {
        data: this.sorted().map((r) => r.price),
        borderColor: '#e31837',
        backgroundColor: '#e31837',
        pointBackgroundColor: '#fff',
        pointBorderColor: '#e31837',
        pointRadius: 4,
        borderWidth: 2,
        tension: 0,
      },
    ],
  }));

  readonly options: ChartConfiguration<'line'>['options'] = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: { display: false },
      tooltip: {
        callbacks: {
          title: (items) => {
            const r = this.sorted()[items[0].dataIndex];
            return r ? fullDate.format(new Date(r.date)) : '';
          },
          label: (item) => `$${(item.parsed.y ?? 0).toFixed(2)}`,
        },
      },
    },
    scales: {
      y: { ticks: { callback: (v) => `$${Number(v).toFixed(0)}` } },
    },
  };
}
