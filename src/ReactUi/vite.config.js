import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

const gatewayUrl = process.env.GATEWAY_URL;

if (!gatewayUrl) {
  throw new Error('GATEWAY_URL must be provided by the AppHost.');
}

function makeContainerReachable(url) {
  const target = new URL(url);

  if (target.hostname === 'localhost' || target.hostname === '127.0.0.1') {
    target.hostname = 'host.docker.internal';
  }

  return target.toString();
}

export default defineConfig({
  plugins: [react()],
  server: {
    host: '0.0.0.0',
    proxy: {
      '/api': {
        target: makeContainerReachable(gatewayUrl),
        changeOrigin: true,
        secure: false
      }
    }
  }
});
