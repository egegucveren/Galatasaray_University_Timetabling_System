// GSÜ Bilgisayar Mühendisliği öğretim programı (ects.gsu.edu.tr) kurallarına göre,
// yarıyıl + seçmeli grup başına seçilmesi gereken ders sayısı. Yalnızca anlık kullanıcı
// arayüzü geri bildirimi (checkbox kilitleme, "x/N seçildi") için kullanılır — asıl
// doğrulama sunucu tarafında (MySqlScheduleRepository.ValidateElectiveSelection) yapılır.
// Bu tablo o taraftaki ElectiveGroupRequiredCounts ile senkron tutulmalıdır.
const ELECTIVE_GROUP_REQUIRED_COUNTS: Record<string, number> = {
  "3|Sosyal-3": 1,
  "5|Teknik-5": 1,
  "6|Teknik-6": 2,
  "7|Teknik-7": 1,
  "7|Sosyal-7": 1,
  "8|INF-8": 2,
  "8|IND-8": 1,
  "8|CNT-8": 1,
};

const DEFAULT_REQUIRED_COUNT = 1;

export function getRequiredElectiveCount(semester: number, group: string): number {
  return ELECTIVE_GROUP_REQUIRED_COUNTS[`${semester}|${group}`] ?? DEFAULT_REQUIRED_COUNT;
}
