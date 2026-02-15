import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import ignoreDynamicImports from 'vite-plugin-ignore-dynamic-imports';

export default defineConfig({
    plugins: [
        react(),
        ignoreDynamicImports({
            include: [
                "src/csharp-wasm/build/**/*.js",
                "src/csharp-wasm/build/**/*.mjs"
            ]
        }),
    ],
    build: {
        outDir: 'build', // CRA's default build output
    },
    base: "/mapartcraft/",
    assetsInclude: [
        "**/*.dll",
    ],
    worker: {
        format: "es",
    },
});