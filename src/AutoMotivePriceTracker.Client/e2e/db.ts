// Search runs against the docker-compose Elasticsearch, in its own index so dev data is untouched.
export const esIndex = process.env['E2E_ES_INDEX'] ?? 'products_e2e';
export const esUrl = process.env['E2E_ES_URL'] ?? 'http://localhost:9200';
export const apiUrl = 'http://localhost:5099';

// A separate database on the docker-compose PostgreSQL server (`docker compose up -d --wait`).
// The API creates and migrates it on startup; global-setup then seeds it with fixed data.
const env = process.env;

export const pgConfig = {
  host: env['E2E_PG_HOST'] ?? 'localhost',
  port: Number(env['E2E_PG_PORT'] ?? 5432),
  user: env['E2E_PG_USER'] ?? 'postgres',
  password: env['E2E_PG_PASSWORD'] ?? 'postgres',
  database: env['E2E_PG_DATABASE'] ?? 'autoprice_e2e',
};

/** The same database as an Npgsql connection string, for the API under test. */
export const dotnetConnectionString =
  `Host=${pgConfig.host};Port=${pgConfig.port};Database=${pgConfig.database};` +
  `Username=${pgConfig.user};Password=${pgConfig.password}`;
