import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  server: {
    host: true,
    port: 8081,
    proxy: {
      '/api': {
        target: 'http://localhost:8080',
        changeOrigin: true
      },
      '/hubs': {
        target: 'http://localhost:8080',
        changeOrigin: true,
        ws: true
      }
    }
  },
  plugins: [react()]
});
