import { RenderMode, ServerRoute } from '@angular/ssr';

export const serverRoutes: ServerRoute[] = [
  // Admin polls the API with timers and is not worth indexing: render it in the browser only.
  { path: 'admin', renderMode: RenderMode.Client },
  // Everything else depends on live API data, so render per request rather than at build time.
  { path: '**', renderMode: RenderMode.Server },
];
