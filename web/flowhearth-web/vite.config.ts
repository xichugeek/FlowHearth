import vue from '@vitejs/plugin-vue'
import Components from 'unplugin-vue-components/vite'
import { ElementPlusResolver } from 'unplugin-vue-components/resolvers'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [
    vue(),
    Components({
      dts: 'src/types/components.d.ts',
      resolvers: [ElementPlusResolver()],
    }),
  ],
  server: {
    host: '127.0.0.1',
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5100',
        changeOrigin: false,
      },
      '/health': {
        target: 'http://127.0.0.1:5100',
        changeOrigin: false,
      },
    },
  },
  test: {
    environment: 'happy-dom',
    include: ['src/**/*.spec.ts'],
    restoreMocks: true,
    server: {
      deps: {
        inline: ['element-plus'],
      },
    },
  },
})
