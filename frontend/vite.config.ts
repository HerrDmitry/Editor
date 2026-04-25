/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // Use relative paths so assets resolve correctly when served from
  // Photino's embedded resource handler (file:// or custom scheme).
  base: './',
  build: {
    // Output to dist/ which will be copied to C# wwwroot/
    outDir: 'dist',
    emptyOutDir: true,
    rollupOptions: {
      output: {
        // Use stable filenames (no content hashes) so App.razor can
        // reference the bundle with a known path.
        entryFileNames: 'assets/index.js',
        chunkFileNames: 'assets/[name].js',
        assetFileNames: 'assets/[name][extname]',
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
  },
})
