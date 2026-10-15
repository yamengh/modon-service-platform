/// <reference types="vitest/config" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The dev server forwards /api to the Modon.Platform backend, so the browser sees one origin
// and the FormCMS login cookie works without any CORS setup.
const apiTarget = process.env.MODON_API_URL ?? "http://localhost:5100";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/api": { target: apiTarget, changeOrigin: false },
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test/setup.ts"],
  },
});
