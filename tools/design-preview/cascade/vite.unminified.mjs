import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Preview only: the generated API client is built from the server's OpenAPI file, which needs .NET.
const stub = { name: 'stub-generated-sdk',
  resolveId(source) { return source.endsWith('generated/v1/sdk.gen') ? '\0stub-sdk' : null; },
  load(id) { return id === '\0stub-sdk' ? 'export const uploadLogicModulePackage = async () => ({}); export const getLogicModulePackageState = async () => ({});' : null; } };
export default defineConfig({ root: process.cwd(), plugins: [react(), stub], build: { cssMinify: false, minify: false } });
