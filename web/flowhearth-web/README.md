# FlowHearth Web

Vue 3, TypeScript and Vite single-page application for FlowHearth.

## Commands

```powershell
npm ci
npm run dev
npm run lint
npm run test:run
npm run typecheck
npm run build
```

The development server listens on `127.0.0.1:5173` and proxies `/api` and
`/health` to the local ASP.NET Core API on `127.0.0.1:5100`.

Element Plus components are imported on demand by `unplugin-vue-components`.
The generated `src/types/components.d.ts` is committed so a clean checkout can
type-check before Vite runs.

Authentication state must not be persisted as a long-lived token in
`localStorage`. The application uses same-origin HttpOnly cookie authentication and an
antiforgery header/cookie pair.
