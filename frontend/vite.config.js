import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Proxy API + SignalR hub to the .NET backend so the browser talks to one origin.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5099', changeOrigin: true },
      '/hub': { target: 'http://localhost:5099', changeOrigin: true, ws: true },
      '/uploads': { target: 'http://localhost:5099', changeOrigin: true },
    },
  },
})
