import { useState } from "react";
import AdminPanel from "@/components/AdminPanel";
import StudentPanel from "@/components/StudentPanel";
import TeacherPanel from "@/components/TeacherPanel";

const TABS = [
  { id: "admin",   label: "Admin" },
  { id: "student", label: "Öğrenci" },
  { id: "teacher", label: "Öğretmen" },
] as const;

type TabId = (typeof TABS)[number]["id"];

export default function App() {
  const [active, setActive] = useState<TabId>("admin");

  return (
    <div className="min-h-screen bg-bg-page">
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
          <img
            src="/gsu.png"
            alt="GSÜ"
            className="h-20 w-20 object-contain"
          />
        </div>
        <div style={{ height: "3px", background: "linear-gradient(to right, #E30A17, #F5C300)" }} />
      </header>

      {/* Page */}
      <main className="max-w-[1200px] mx-auto px-4 py-8 pb-16">
        {/* Tab bar */}
        <div className="flex gap-1 mb-6 bg-white rounded-xl shadow-card p-1 w-fit">
          {TABS.map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActive(tab.id)}
              className={`px-5 py-2 rounded-lg text-sm font-semibold transition-all ${
                active === tab.id
                  ? "bg-primary text-white shadow-sm"
                  : "text-muted hover:text-ink hover:bg-gray-50"
              }`}
            >
              {tab.label}
            </button>
          ))}
        </div>

        {active === "admin"   && <AdminPanel />}
        {active === "student" && <StudentPanel />}
        {active === "teacher" && <TeacherPanel />}
      </main>

      {/* Footer */}
      <footer className="border-t border-line py-4 text-center text-xs text-muted">
        © 2026 GSÜ Yazılım
      </footer>
    </div>
  );
}
