import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import { playwright } from '@vitest/browser-playwright';
export default defineConfig({
    plugins: [react()],
    resolve: { dedupe: ['react', 'react-dom'] },
    optimizeDeps: { include: ['react', 'react-dom/client', '@testing-library/react', 'react-bootstrap', 'react-bootstrap/Button', 'prop-types'] },
    test: {
        include: ['src/Components/ToDo/AnalysisResults/GeneResultFigures.test.jsx', 'src/Components/Pages/ExploreData.test.jsx', 'src/api/downloadGenomeAPICall.test.js', 'src/utils/exploreSpecies.test.js', 'src/Components/ToDo/AnalysisResults/AnalysisResults.lazy.test.jsx', 'src/Components/ToDo/AnalysisResults/AnalysisResults.demo.test.jsx'],
        browser: {
            enabled: true, headless: true,
            provider: playwright({ launchOptions: { channel: 'chrome' } }),
            instances: [{ browser: 'chromium' }],
        },
    },
});
