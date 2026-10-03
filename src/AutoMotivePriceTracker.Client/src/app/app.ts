import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  styles: `
    .top-row { background: #1a1a2e; padding: 0.75rem 1.5rem; display: flex; align-items: center; gap: 2rem; flex-wrap: wrap; }
    .navbar-brand { color: #fff; font-size: 1.25rem; font-weight: 700; text-decoration: none; }
    nav { display: flex; gap: 0.25rem; }
    nav a { color: #ccc; padding: 0.5rem 1rem; text-decoration: none; border-radius: 4px; }
    nav a:hover, nav a.active { color: #fff; background: rgba(255, 255, 255, 0.1); }
  `,
  template: `
    <header class="top-row">
      <a class="navbar-brand" routerLink="/">🔧 AutoPrice NZ</a>
      <nav>
        <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">Home</a>
        <a routerLink="/products" routerLinkActive="active">Products</a>
        <a routerLink="/admin" routerLinkActive="active">Admin</a>
      </nav>
    </header>
    <main class="main-content"><router-outlet /></main>
  `,
})
export class App {}
