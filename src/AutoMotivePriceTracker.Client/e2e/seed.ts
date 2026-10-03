import { Client, ClientConfig } from 'pg';

const REPCO = 0;
const SUPERCHEAP = 1;

interface SeedProduct {
  id: number;
  name: string;
  brand: string | null;
  category: string;
  sku: string;
  retailer: number;
  lastChecked: string;
  history: { price: string; was: string | null; date: string }[];
}

export const products: SeedProduct[] = [
  {
    id: 1, name: 'Penrite Racing 15W-50 Engine Oil 5L', brand: 'Penrite', category: 'engine-oil', sku: 'REP-1',
    retailer: REPCO, lastChecked: '2026-03-04 00:00:00',
    history: [
      { price: '160.00', was: null, date: '2026-01-01 00:00:00' },
      { price: '153.00', was: null, date: '2026-02-01 00:00:00' },
      { price: '114.75', was: '153.00', date: '2026-03-01 00:00:00' },
    ],
  },
  {
    id: 2, name: 'Bendix Brake Pads Front', brand: 'Bendix', category: 'brake-pads', sku: 'SCA-2',
    retailer: SUPERCHEAP, lastChecked: '2026-03-03 00:00:00',
    history: [{ price: '59.99', was: null, date: '2026-03-01 00:00:00' }],
  },
  {
    id: 3, name: 'Castrol Edge 5W-30 Engine Oil 5L', brand: null, category: 'engine-oil', sku: 'SCA-3',
    retailer: SUPERCHEAP, lastChecked: '2026-03-02 00:00:00',
    history: [
      { price: '110.00', was: null, date: '2026-02-01 00:00:00' },
      { price: '89.00', was: '110.00', date: '2026-03-01 00:00:00' },
    ],
  },
  {
    id: 4, name: 'Ryco Oil Filter Z145A', brand: 'Ryco', category: 'oil-filters', sku: 'REP-4',
    retailer: REPCO, lastChecked: '2026-03-01 00:00:00',
    history: [{ price: '12.50', was: null, date: '2026-03-01 00:00:00' }],
  },
];

/** Replaces the contents of the (already migrated) e2e database with the fixed data set above. */
export async function seed(config: ClientConfig): Promise<void> {
  const db = new Client(config);
  await db.connect();
  try {
    await db.query('BEGIN');
    await db.query('TRUNCATE TABLE "PriceRecords", "Products" RESTART IDENTITY CASCADE');
    for (const p of products) {
      await db.query(
        `INSERT INTO "Products" ("Id", "Name", "Brand", "Category", "Sku", "ImageUrl", "SourceUrl", "Retailer", "LastChecked")
         VALUES ($1, $2, $3, $4, $5, NULL, $6, $7, $8)`,
        [p.id, p.name, p.brand, p.category, p.sku, `https://example.test/${p.sku}`, p.retailer, `${p.lastChecked}Z`],
      );
      for (const h of p.history) {
        await db.query(
          'INSERT INTO "PriceRecords" ("ProductId", "Price", "WasPrice", "Date") VALUES ($1, $2, $3, $4)',
          [p.id, h.price, h.was, `${h.date}Z`],
        );
      }
    }
    // Ids were inserted explicitly; keep the identity sequence in step.
    await db.query(`SELECT setval(pg_get_serial_sequence('"Products"', 'Id'), (SELECT MAX("Id") FROM "Products"))`);
    await db.query('COMMIT');
  } catch (e) {
    await db.query('ROLLBACK');
    throw e;
  } finally {
    await db.end();
  }
}
