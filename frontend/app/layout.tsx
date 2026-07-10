import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "GSÜ Ders Programı",
  description: "Galatasaray Üniversitesi Ders Programı Yönetim Sistemi",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="tr">
      <body className="min-h-screen bg-bg-page">
        {/* Header */}
        <header className="bg-white shadow-sm border-b border-line">
          <div className="max-w-[1200px] mx-auto px-6 h-24 flex items-center justify-between">
            <div className="flex items-center gap-3">
              <span className="text-2xl font-black tracking-tight select-none">
                <span style={{ color: "#E30A17" }}>G</span>
                <span style={{ color: "#F5C300" }}>S</span>
                <span style={{ color: "#1a1a1a" }}>Ü</span>
                <span className="font-semibold ml-2 text-xl text-ink"> Ders Programı</span>
              </span>
            </div>

            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src="/gsu.png"
              alt="GSÜ"
              className="h-20 w-20 object-contain drop-shadow-md"
            />
          </div>
          <div style={{ height: "3px", background: "linear-gradient(to right, #E30A17, #F5C300)" }} />
        </header>

        {/* Page */}
        <main className="max-w-[1200px] mx-auto px-4 py-8 pb-16">
          {children}
        </main>

        {/* Footer */}
        <footer className="border-t border-line py-4 text-center text-xs text-muted">
          © 2026 GSÜ Yazılım
        </footer>
      </body>
    </html>
  );
}
