import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

// Component tests run in jsdom against a fake backend; nothing here can reach a broker.
export default defineConfig({
  plugins: [react()],
  test: { environment: 'jsdom', css: true, include: ['src/**/*.test.tsx'], reporters: ['default', 'junit'], outputFile: { junit: 'test-results/vitest-junit.xml' } },
})
