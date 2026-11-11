import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import { playwright } from '@vitest/browser-playwright';
export default defineConfig({
    plugins: [react()],
    test: {
        include: ['src/Components/Pages/ExploreData.test.jsx', 'src/api/downloadGenomeAPICall.test.js', 'src/utils/exploreSpecies.test.js'],
        browser: {
            enabled: true, headless: true,
            provider: playwright({ launchOptions: { channel: 'chrome' } }),
            instances: [{ browser: 'chromium' }],
        },
    },
});
