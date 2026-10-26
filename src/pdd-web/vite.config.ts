import react, { reactCompilerPreset } from '@vitejs/plugin-react'
import babel from '@rolldown/plugin-babel'
import { defineConfig } from 'vitest/config'

const apiUrl = process.env.PDD_API_URL ?? 'http://localhost:5105'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    babel({ presets: [reactCompilerPreset()] })
  ],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': { target: apiUrl, changeOrigin: true },
    },
  },
  test: {
    include: ['../../tests/pdd-web/**/*.test.{ts,tsx}'],
  },
})
