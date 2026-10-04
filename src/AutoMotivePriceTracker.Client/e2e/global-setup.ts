import { apiUrl, pgConfig } from './db';
import { seed } from './seed';

// Runs after the webServers are up, so the API has already created/migrated the e2e database.
export default async function globalSetup(): Promise<void> {
  await seed(pgConfig);

  // The API indexed an empty database when it started; index the seeded products now.
  const res = await fetch(`${apiUrl}/api/admin/reindex`, { method: 'POST' });
  if (!res.ok) throw new Error(`Search reindex failed: ${res.status} ${await res.text()}`);
}
