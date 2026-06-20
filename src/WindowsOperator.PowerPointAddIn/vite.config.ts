import { copyFileSync, cpSync, mkdirSync } from "node:fs";
import { resolve } from "node:path";
import basicSsl from "@vitejs/plugin-basic-ssl";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [
    basicSsl(),
    {
      name: "copy-office-assets",
      closeBundle() {
        mkdirSync("dist/assets", { recursive: true });
        cpSync("assets", "dist/assets", { recursive: true });
        copyFileSync("manifest.xml", "dist/manifest.xml");
      },
    },
  ],
  server: {
    https: {},
    port: 3003,
    strictPort: true,
  },
  preview: {
    https: {},
    port: 3003,
    strictPort: true,
  },
  build: {
    sourcemap: true,
    outDir: "dist",
    rollupOptions: {
      input: {
        index: resolve(__dirname, "index.html"),
        taskpane: resolve(__dirname, "taskpane.html"),
        commands: resolve(__dirname, "commands.html"),
      },
    },
  },
});
