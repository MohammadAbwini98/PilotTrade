import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  // The API serves and publishes this folder, so a frontend build always reaches the packaged app.
  build: { outDir: '../PilotTrade.Api/wwwroot', emptyOutDir: true },
  server: { proxy: { '/api': 'http://127.0.0.1:5099', '/health': 'http://127.0.0.1:5099' } }
})
