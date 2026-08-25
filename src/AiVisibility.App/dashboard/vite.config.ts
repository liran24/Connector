import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],

  // Built straight into the ASP.NET app's wwwroot, so one `dotnet run` serves both the
  // dashboard and its API from the same origin — which is what App Bridge expects.
  build: {
    outDir: "../wwwroot",
    emptyOutDir: true,
  },

  // `npm run dev` gives hot reload while proxying API calls to the running backend.
  server: {
    port: 5173,
    proxy: {
      "/api": "http://localhost:5299",
    },
  },
});
