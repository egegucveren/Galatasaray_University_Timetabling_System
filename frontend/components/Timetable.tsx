import type { ScheduleResult, TimeSlot } from "@/lib/types";

const DAYS = ["Pazartesi", "Sali", "Carsamba", "Persembe", "Cuma"];
const DAY_LABELS: Record<string, string> = {
  Pazartesi: "Pazartesi", Sali: "Salı", Carsamba: "Çarşamba", Persembe: "Perşembe", Cuma: "Cuma",
};

function minutesFrom(clock: string) {
  const [h, m] = clock.split(":").map(Number);
  return h * 60 + m;
}
function clockFrom(minutes: number) {
  return `${String(Math.floor(minutes / 60)).padStart(2, "0")}:${String(minutes % 60).padStart(2, "0")}`;
}
function splitRange(range: string) {
  const [start, end] = range.split("-").map((p) => p.trim());
  return { start, end, startMin: minutesFrom(start), endMin: minutesFrom(end) };
}

interface Props {
  items: ScheduleResult[];
  timeSlots: TimeSlot[];
  mode: "admin" | "student" | "teacher";
  printTitle?: string;
}

// ── PDF / Print export ────────────────────────────────────────────────────────

function buildPrintTable(
  items: ScheduleResult[],
  days: string[],
  timeline: { startMin: number; endMin: number; label: string }[],
  mode: "admin" | "student" | "teacher"
) {
  type SlotItem = ScheduleResult & { startMin: number; endMin: number };
  const byDay: Record<string, SlotItem[]> = {};
  for (const day of days) byDay[day] = [];
  for (const item of items) {
    const r = splitRange(item.HourRange);
    if (byDay[item.Day]) byDay[item.Day].push({ ...item, ...r });
  }
  for (const day of days) {
    byDay[day].sort((a, b) => a.startMin - b.startMin || a.RoomName.localeCompare(b.RoomName));
  }

  // Not: burada satır birleştirme (rowspan) YAPILMIYOR. Önceki sürüm bir hücrede
  // başlayan derslerden sadece ilkini yazdırıp aynı gün/saatte başlayan diğer
  // tüm dersleri (örn. "Tüm Yarıyıllar" görünümünde farklı yarıyılların aynı
  // saatteki dersleri) sessizce siliyordu. Ekrandaki etkileşimli tabloyla aynı
  // mantık kullanılıyor: her satır+gün hücresinde o an devam eden derslerin
  // TAMAMI alt alta listelenir.
  let html = `<thead><tr><th>Saat</th>${days.map((d) => `<th>${DAY_LABELS[d] ?? d}</th>`).join("")}</tr></thead><tbody>`;
  for (let ri = 0; ri < timeline.length; ri++) {
    const row = timeline[ri];
    html += "<tr>";
    html += `<td class="time">${clockFrom(row.startMin)}-${clockFrom(row.endMin)}</td>`;
    for (let di = 0; di < days.length; di++) {
      const day = days[di];
      const matches = byDay[day].filter((s) => s.startMin <= row.startMin && row.startMin < s.endMin);
      if (matches.length === 0) { html += "<td></td>"; continue; }

      const entries = matches.map((item) => {
        const roomLabel = `${item.RoomName}${item.IsAmphi ? " (Amfi)" : ""}`;
        const teacher = item.TeacherName;
        const semesterLabel = `${item.Semester}. Yarıyıl`;
        const suffix = item.IsElective ? " (S)" : "";
        const metaLines =
          mode === "teacher" ? [semesterLabel, roomLabel]
          : mode === "student" ? [teacher, roomLabel]
          : [semesterLabel, teacher, roomLabel];
        return `<div class="entry">
          <strong>${item.CourseTitle}${suffix}</strong>
          ${metaLines.map((l) => `<br/><span>${l}</span>`).join("")}
        </div>`;
      }).join("");

      html += `<td class="course">${entries}</td>`;
    }
    html += "</tr>";
  }
  html += "</tbody>";
  return html;
}

// GSÜ'nün resmi ders programı ilanlarında olduğu gibi: yazdırılan program,
// tüm yarıyılları tek bir dev tabloda birleştirmek yerine her yarıyıl için
// ayrı bir tabloya bölünür (1. Yarıyıl, 2. Yarıyıl, ... 8. Yarıyıl).
function semesterLabel(semester: number) {
  const fall = semester % 2 === 1;
  return `${semester}. Yarıyıl (${fall ? "Güz" : "Bahar"})`;
}

function printSchedule(
  items: ScheduleResult[],
  days: string[],
  timeline: { startMin: number; endMin: number; label: string }[],
  title: string,
  mode: "admin" | "student" | "teacher"
) {
  const semesters = Array.from(new Set(items.map((i) => i.Semester))).sort((a, b) => a - b);
  const colWidth = `${Math.floor(85 / days.length)}%`;

  // Birden fazla yarıyıl aynı çıktıda varsa (örn. admin "Tüm Yarıyıllar" görünümü),
  // her yarıyıl kendi başlığıyla ayrı bir tabloya ve ayrı bir sayfaya bölünür.
  const sections = semesters.length > 1
    ? semesters.map((s) => {
        const semItems = items.filter((i) => i.Semester === s);
        return `<div class="year-section">
          <h2>${semesterLabel(s)}</h2>
          <table>${buildPrintTable(semItems, days, timeline, mode)}</table>
        </div>`;
      }).join("")
    : `<table>${buildPrintTable(items, days, timeline, mode)}</table>`;

  const html = `<!DOCTYPE html>
<html lang="tr">
<head>
  <meta charset="UTF-8"/>
  <title>${title}</title>
  <style>
    * { margin: 0; padding: 0; box-sizing: border-box; }
    body { font-family: "Times New Roman", Times, serif; padding: 24px; }
    h1 { text-align: center; font-size: 16pt; margin-bottom: 16px; letter-spacing: 1px; }
    h2 { text-align: center; font-size: 12.5pt; margin: 0 0 10px; letter-spacing: 0.5px; }
    .year-section { page-break-after: always; }
    .year-section:last-child { page-break-after: auto; }
    table { width: 100%; border-collapse: collapse; }
    th, td { border: 1px solid #222; padding: 6px 8px; text-align: center; vertical-align: top; }
    th { font-size: 9pt; font-weight: bold; background: #f0f0f0; }
    tbody tr { min-height: 64px; }
    td.time { width: 70px; font-size: 8pt; font-weight: bold; white-space: nowrap; vertical-align: middle; }
    td.course { font-size: 8pt; }
    td.course .entry { padding-bottom: 4px; margin-bottom: 4px; border-bottom: 1px dashed #999; }
    td.course .entry:last-child { padding-bottom: 0; margin-bottom: 0; border-bottom: none; }
    td.course strong { display: block; font-size: 8.5pt; margin-bottom: 3px; }
    td.course span { display: block; font-size: 7.5pt; color: #333; }
    th:first-child, td.time { width: 80px; }
    th:not(:first-child) { width: ${colWidth}; }
    @media print { body { padding: 10px; } @page { size: A4 landscape; margin: 10mm; } tr { page-break-inside: avoid; } }
  </style>
</head>
<body>
  <h1>${title}</h1>
  ${sections}
  <script>window.onload = function() { window.print(); }<\/script>
</body>
</html>`;

  const win = window.open("", "_blank");
  if (!win) { alert("Popup engelleyici izin ver."); return; }
  win.document.write(html);
  win.document.close();
}

// ─────────────────────────────────────────────────────────────────────────────

export default function Timetable({ items, timeSlots, mode, printTitle }: Props) {
  if (!timeSlots.length) {
    return <p className="text-sm text-muted italic p-2">Zaman dilimleri yükleniyor…</p>;
  }

  const knownDays = new Set(timeSlots.map((s) => s.Day));
  const days = DAYS.filter((d) => knownDays.has(d));

  const bounds = timeSlots.map((s) => splitRange(s.HourRange));
  const minStart = Math.min(...bounds.map((b) => b.startMin));
  const maxEnd   = Math.max(...bounds.map((b) => b.endMin));

  const timeline: { startMin: number; endMin: number; label: string }[] = [];
  for (let m = minStart; m < maxEnd; m += 60) {
    timeline.push({ startMin: m, endMin: m + 60, label: `${clockFrom(m)}–${clockFrom(m + 60)}` });
  }

  if (!items.length && mode !== "admin") {
    return <p className="text-sm text-muted italic p-2">Kayıt bulunamadı.</p>;
  }

  const byDay: Record<string, (ScheduleResult & { startMin: number; endMin: number })[]> = {};
  for (const day of days) byDay[day] = [];
  for (const item of items) {
    const r = splitRange(item.HourRange);
    if (byDay[item.Day]) byDay[item.Day].push({ ...item, ...r });
  }
  for (const day of days) {
    byDay[day].sort((a, b) => a.startMin - b.startMin || a.RoomName.localeCompare(b.RoomName));
  }

  const title = printTitle ?? "GSÜ Ders Programı";

  return (
    <div>
      {/* PDF export button */}
      {items.length > 0 && (
        <div className="flex justify-end mb-3">
          <button
            className="btn btn-secondary text-xs py-1.5 px-3 flex items-center gap-1.5"
            onClick={() => printSchedule(items, days, timeline, title, mode)}
          >
            <svg xmlns="http://www.w3.org/2000/svg" className="w-3.5 h-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M6 9V2h12v7M6 18H4a2 2 0 01-2-2v-5a2 2 0 012-2h16a2 2 0 012 2v5a2 2 0 01-2 2h-2"/>
              <rect x="6" y="14" width="12" height="8"/>
            </svg>
            PDF / Yazdır
          </button>
        </div>
      )}

      <div className="overflow-x-auto rounded-lg border border-line">
        <table className="w-full border-collapse min-w-[560px] bg-white text-sm table-fixed">
          <thead>
            <tr>
              <th className="border-b border-r border-line px-2 py-3 bg-gray-50 text-xs font-semibold text-muted uppercase tracking-wider w-20 text-center">
                Saat
              </th>
              {days.map((day) => (
                <th key={day} className="border-b border-r border-line px-3 py-3 bg-gray-50 text-xs font-semibold text-ink uppercase tracking-wider text-center last:border-r-0">
                  {DAY_LABELS[day] ?? day}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {timeline.map((row, rowIdx) => (
              <tr key={row.startMin} style={{ height: "90px" }} className={rowIdx % 2 === 0 ? "bg-white" : "bg-gray-50/40"}>
                <td className="border-b border-r border-line px-1 py-2 text-center text-[0.68rem] font-semibold text-muted whitespace-nowrap align-middle">
                  {row.label}
                </td>
                {days.map((day) => {
                  const matches = byDay[day]?.filter(
                    (s) => s.startMin <= row.startMin && row.startMin < s.endMin
                  ) ?? [];

                  if (matches.length === 0) {
                    return <td key={day} className="border-b border-r border-line last:border-r-0" />;
                  }

                  return (
                    <td key={day} className="border-b border-r border-line last:border-r-0 p-1.5 align-middle">
                      <div className="flex flex-col gap-1">
                        {matches.map((match) => {
                          const roomLabel = `${match.RoomName}${match.IsAmphi ? " (Amfi)" : ""}`;
                          const semesterLabel = `${match.Semester}. Yarıyıl`;
                          const meta =
                            mode === "teacher" ? [semesterLabel, roomLabel]
                            : mode === "student" ? [match.TeacherName, roomLabel]
                            : [semesterLabel, match.TeacherName, roomLabel];

                          return (
                            <div key={`${match.CourseId}-${match.RoomId}`} className="flex flex-col gap-0.5 items-center text-center bg-primary/5 border border-primary/20 rounded-lg px-2 py-1.5">
                              <span className="font-semibold text-ink text-xs leading-snug">
                                {match.CourseTitle}
                              </span>
                              {meta.map((line, i) => (
                                <span key={i} className="text-[0.68rem] text-muted leading-snug">{line}</span>
                              ))}
                              <div className="flex justify-center gap-1 flex-wrap mt-0.5">
                                {mode === "student" && (
                                  <span className={`text-[0.6rem] px-1.5 py-0.5 rounded font-medium ${match.IsElective ? "bg-primary/10 text-primary" : "bg-gray-100 text-muted"}`}>
                                    {match.IsElective ? "Seçmeli" : "Zorunlu"}
                                  </span>
                                )}
                                {mode === "admin" && (
                                  <span className={`text-[0.6rem] px-1.5 py-0.5 rounded font-medium ${match.IsManual ? "bg-amber-100 text-amber-700" : "bg-gray-100 text-muted"}`}>
                                    {match.IsManual ? "Manuel" : "Otomatik"}
                                  </span>
                                )}
                              </div>
                            </div>
                          );
                        })}
                      </div>
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
