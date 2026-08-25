/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Shopify app API key, injected at build time. Public by design. */
  readonly VITE_SHOPIFY_API_KEY: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
