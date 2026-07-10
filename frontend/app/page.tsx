"use client";

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

export default function Home() {
  const [active, setActive] = useState<TabId>("admin");

  return (
    <>
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
    </>
  );
}
