import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import path from "path";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./"),
    },
  },
  build: {
    outDir: "out",
    emptyOutDir: true,
  },
  server: {
    proxy: {
      "/api": "http://127.0.0.1:5038",
    },
    // Proje klasörü macOS'ta iCloud/Desktop senkronizasyonu altında olduğunda
    // (veya ağ paylaşımlı bir klasörde), native dosya sistemi olaylarına dayalı
    // izleme (fsevents) bazen değişiklikleri kaçırıyor ve Vite dev sunucusunun
    // elle yeniden başlatılması gerekiyormuş gibi görünüyor. Polling'e geçmek
    // (belirli aralıklarla dosyaları kontrol etmek) bunu güvenilir şekilde çözer.
    watch: {
      usePolling: true,
      interval: 300,
    },
  },
});
