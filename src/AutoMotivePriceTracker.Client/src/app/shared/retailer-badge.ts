import { Component, input } from '@angular/core';
import { Retailer } from '../models/models';

@Component({
  selector: 'app-retailer-badge',
  template: `
    @if (retailer() === 'Repco') {
      <span class="badge badge-repco">Repco</span>
    } @else {
      <span class="badge badge-supercheap">SupercheapAuto</span>
    }
  `,
})
export class RetailerBadge {
  readonly retailer = input.required<Retailer>();
}
