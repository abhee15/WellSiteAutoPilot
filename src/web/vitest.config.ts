import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    include: ['tests/interaction/**/*.test.tsx'],
    restoreMocks: true,
    clearMocks: true
  }
});
