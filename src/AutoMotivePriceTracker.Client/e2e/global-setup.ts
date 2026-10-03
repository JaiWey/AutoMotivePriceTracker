import { pgConfig } from './db';
import { seed } from './seed';

// Runs after the webServers are up, so the API has already created/migrated the e2e database.
export default async function globalSetup(): Promise<void> {
  await seed(pgConfig);
}
