import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import { resolve } from 'node:path';

export default defineConfig({
    plugins: [
        vue()
    ],

    define: {
        'process.env.NODE_ENV': JSON.stringify('production')
    },

    build: {
        lib: {
            entry: resolve(
                import.meta.dirname,
                'src/plugin.ts'
            ),

            formats: [
                'es'
            ],

            fileName: () =>
                'scalar-client-plugin.js'
        },

        outDir: resolve(
            import.meta.dirname,
            '../Assets'
        ),

        emptyOutDir: false,

        minify: true
    }
});