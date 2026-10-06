import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import { join } from 'node:path';

const browserDistFolder = join(import.meta.dirname, '../browser');

const app = express();
// Behind CloudFront (AWS deployment) the public host arrives in X-Viewer-Host, set by a CloudFront function
// (functions may not set X-Forwarded-*), and the scheme is always https. Angular ignores X-Forwarded-* unless
// trusted, and trusting them is only safe when the server is reachable solely through that proxy, so it is
// opt-in. Hosts are still validated against NG_ALLOWED_HOSTS.
const trustProxyHeaders = process.env['TRUST_PROXY_HEADERS'] === 'true' ? ['x-forwarded-host', 'x-forwarded-proto'] : false;
const angularApp = new AngularNodeAppEngine({ trustProxyHeaders });

if (trustProxyHeaders) {
  app.use((req, _res, next) => {
    const viewerHost = req.headers['x-viewer-host'];
    if (typeof viewerHost === 'string' && viewerHost) {
      req.headers['x-forwarded-host'] = viewerHost;
      req.headers['x-forwarded-proto'] = 'https';
    }
    next();
  });
}

/**
 * Example Express Rest API endpoints can be defined here.
 * Uncomment and define endpoints as necessary.
 *
 * Example:
 * ```ts
 * app.get('/api/{*splat}', (req, res) => {
 *   // Handle API request
 * });
 * ```
 */

/**
 * Serve static files from /browser
 */
app.use(
  express.static(browserDistFolder, {
    maxAge: '1y',
    index: false,
    redirect: false,
  }),
);

/**
 * Handle all other requests by rendering the Angular application.
 */
app.use((req, res, next) => {
  angularApp
    .handle(req)
    .then((response) => (response ? writeResponseToNodeResponse(response, res) : next()))
    .catch(next);
});

/**
 * Start the server if this module is the main entry point, or it is ran via PM2.
 * The server listens on the port defined by the `PORT` environment variable, or defaults to 4000.
 */
if (isMainModule(import.meta.url) || process.env['pm_id']) {
  const port = process.env['PORT'] || 4000;
  app.listen(port, (error) => {
    if (error) {
      throw error;
    }

    console.log(`Node Express server listening on http://localhost:${port}`);
  });
}

/**
 * Request handler used by the Angular CLI (for dev-server and during build) or Firebase Cloud Functions.
 */
export const reqHandler = createNodeRequestHandler(app);
