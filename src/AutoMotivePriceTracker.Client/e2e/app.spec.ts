import { Page, expect, test } from '@playwright/test';

test.describe('Home', () => {
  test('shows deals, recently updated products and retailer stats', async ({ page }) => {
    await page.goto('/');

    await expect(page.getByRole('heading', { name: /AutoPrice NZ/ })).toBeVisible();

    const deals = page.locator('.section', { has: page.getByRole('heading', { name: 'Current Deals' }) });
    await expect(deals.locator('app-product-card')).toHaveCount(2); // the two marked-down products
    await expect(deals).toContainText('Penrite Racing 15W-50 Engine Oil 5L');
    await expect(deals).not.toContainText('Bendix Brake Pads Front');

    const recent = page.locator('.section', { has: page.getByRole('heading', { name: 'Recently Updated' }) });
    await expect(recent.locator('app-product-card')).toHaveCount(4);

    const stats = page.locator('.stats-bar');
    await expect(stats.locator('.stat', { hasText: 'Products tracked' })).toContainText('4');
    await expect(stats.locator('.stat', { hasText: 'Repco' })).toContainText('2');
    await expect(stats.locator('.stat', { hasText: 'Supercheap Auto' })).toContainText('2');
  });

  test('shows sale price, was-price and discount on a deal card', async ({ page }) => {
    await page.goto('/');

    const card = page.locator('app-product-card', { hasText: 'Penrite Racing' }).first();
    await expect(card.locator('.current-price')).toHaveText('$114.75');
    await expect(card.locator('.was-price')).toContainText('$153.00');
    await expect(card.locator('.discount-badge')).toHaveText('-25%');
  });

  test('navigates with the top nav and falls back to home for unknown routes', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('link', { name: 'Products', exact: true }).click();
    await expect(page).toHaveURL(/\/products$/);
    await expect(page.getByRole('heading', { name: 'Products' })).toBeVisible();

    await page.goto('/no-such-page');
    await expect(page).toHaveURL(/\/$/);
  });
});

test.describe('Products', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/products');
    await expect(page.locator('.result-count')).toHaveText('4 products');
  });

  const names = (page: Page) => page.locator('.product-name');

  test('searches by name or brand, case-insensitively', async ({ page }) => {
    await page.getByPlaceholder('Search name or brand...').fill('BENDIX');

    await expect(page.locator('.result-count')).toHaveText('1 products');
    await expect(names(page)).toHaveText(['Bendix Brake Pads Front']);
  });

  test('filters by category', async ({ page }) => {
    await expect(page.locator('select').first().locator('option')).toHaveText([
      'All Categories', 'brake-pads', 'engine-oil', 'oil-filters',
    ]);

    await page.locator('select').first().selectOption('engine-oil');

    await expect(names(page)).toHaveCount(2);
    await expect(page.locator('.product-grid')).toContainText('Castrol Edge');
  });

  test('filters by retailer, combined with category', async ({ page }) => {
    await page.locator('select').nth(1).selectOption('SupercheapAuto');
    await expect(page.locator('.result-count')).toHaveText('2 products');

    await page.locator('select').first().selectOption('engine-oil');
    await expect(names(page)).toHaveText(['Castrol Edge 5W-30 Engine Oil 5L']);
  });

  test('filters to on-sale products only', async ({ page }) => {
    await page.getByLabel('On Sale Only').check();

    await expect(page.locator('.result-count')).toHaveText('2 products');
    await expect(page.locator('.discount-badge')).toHaveCount(2);
  });

  test('shows an empty state when nothing matches', async ({ page }) => {
    await page.getByPlaceholder('Search name or brand...').fill('no such product');

    await expect(page.getByText('No products match your filters.')).toBeVisible();
  });

  test('opens a product from its card', async ({ page }) => {
    await page.locator('app-product-card', { hasText: 'Ryco Oil Filter' }).click();

    await expect(page).toHaveURL(/\/product\/4$/);
    await expect(page.getByRole('heading', { name: 'Ryco Oil Filter Z145A' })).toBeVisible();
  });
});

test.describe('Product detail', () => {
  test('shows details, price chart and records newest first', async ({ page }) => {
    await page.goto('/product/1');

    await expect(page.getByRole('heading', { name: 'Penrite Racing 15W-50 Engine Oil 5L' })).toBeVisible();
    await expect(page.getByText('Brand: Penrite')).toBeVisible();
    await expect(page.locator('.price-row .current-price')).toHaveText('$114.75');
    await expect(page.locator('.price-row .was-price')).toContainText('$153.00');
    await expect(page.getByRole('link', { name: 'View on Repco' })).toHaveAttribute('href', 'https://example.test/REP-1');

    // 3 price points -> a real chart is rendered
    const canvas = page.locator('app-price-chart canvas');
    await expect(canvas).toBeVisible();
    expect((await canvas.boundingBox())!.height).toBeGreaterThan(100);

    const rows = page.locator('.price-history-table tbody tr');
    await expect(rows).toHaveCount(3);
    await expect(rows.nth(0).locator('td').nth(1)).toHaveText('$114.75');
    await expect(rows.nth(0).locator('td').nth(2)).toHaveText('$153.00');
    await expect(rows.nth(0).locator('td').nth(3)).toHaveText('25%');
    await expect(rows.nth(2).locator('td').nth(1)).toHaveText('$160.00');
    await expect(rows.nth(2).locator('td').nth(2)).toHaveText('-');
  });

  test('explains when there is not enough history for a chart', async ({ page }) => {
    await page.goto('/product/2');

    await expect(page.getByRole('heading', { name: 'Bendix Brake Pads Front' })).toBeVisible();
    await expect(page.getByText('Not enough data to display a chart.')).toBeVisible();
    await expect(page.locator('app-price-chart canvas')).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'View on SupercheapAuto' })).toBeVisible();
  });

  test('shows "Product not found." for an unknown id', async ({ page }) => {
    await page.goto('/product/9999');

    await expect(page.getByText('Product not found.')).toBeVisible();
  });
});

test.describe('Admin', () => {
  test('runs a scrape and reports completion', async ({ page }) => {
    await page.goto('/admin');
    const button = page.getByRole('button', { name: 'Run Scrape Now' });
    await expect(button).toBeEnabled();

    await button.click();

    // Scrapers are disabled in the e2e API, so the run completes immediately.
    await expect(page.getByText(/Scrape completed/)).toBeVisible({ timeout: 15_000 });
    await expect(button).toBeEnabled();
  });
});
