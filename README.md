# GSÜ Ders Programlama Sistemi

Galatasaray Üniversitesi Bilgisayar Mühendisliği bölümü için ders programı oluşturma,
öğrenci kaydı/onayı, not girişi ve ön koşul yönetimi yapan bir web uygulaması.

## Teknoloji Yığını

- **Backend:** ASP.NET Core (net10.0) minimal API, MySqlConnector ile ham SQL (ORM yok)
- **Veritabanı:** MySQL
- **Frontend:** Vite + React + TypeScript, Tailwind CSS
- **Zamanlama motoru:** `Services/BacktrackingSchedulerService.cs` içinde özel yazılmış
  bir geri izlemeli (backtracking) yerleştirme algoritması (gerçek Google OR-Tools
  kütüphanesi kullanılmıyor)

## Gereksinimler

- .NET SDK 10
- Node.js 18+ ve npm
- Çalışan bir MySQL sunucusu (3306 portu)

## Kurulum

```bash
# 1. MySQL'in çalıştığından emin olun (varsayılan: 127.0.0.1:3306, kullanıcı: root)
# 2. Backend bağımlılıklarını yükleyin ve derleyin (frontend build'ini de otomatik tetikler)
dotnet build
```

`GsuTimetablingSystem.csproj` içindeki bir MSBuild target'ı, her `dotnet build`/`dotnet run`
öncesinde otomatik olarak `frontend/` klasöründe `npm install` + `npm run build` çalıştırıp
çıktıyı `wwwroot/`'a kopyalar. Yani `wwwroot` içeriği elle düzenlenmemelidir — bir sonraki
derlemede üzerine yazılır.

## Çalıştırma

İki farklı mod var: **prod-benzeri tek komut** ve **geliştirme (HMR'li) iki ayrı süreç**.

### Prod-benzeri (tek komut, HMR yok)

```bash
GSU_MYSQL_PASSWORD='mysql-sifreniz' dotnet run
```

Uygulama `http://127.0.0.1:5038` adresinde açılır ve `wwwroot`'taki (Vite ile önceden
derlenmiş) statik build'i sunar. Frontend'te yaptığınız değişiklikler burada **otomatik
görünmez** — her seferinde `dotnet run`'ın tetiklediği `npm run build` bekler.

### Geliştirme modu (HMR ile canlı yeniden yükleme)

```bash
# Terminal 1 — backend
GSU_MYSQL_PASSWORD='mysql-sifreniz' dotnet run

# Terminal 2 — frontend dev sunucusu
cd frontend
npm run dev
```

Tarayıcıda backend portu (5038) yerine **Vite'ın verdiği adresi** açın (varsayılan
`http://localhost:5173`). `vite.config.ts` `/api` isteklerini otomatik olarak
`http://127.0.0.1:5038`'e proxy'ler, bu port üzerinde kod değişiklikleri anında
(Hot Module Replacement ile) yansır.

## Testler

`GsuTimetablingSystem.Tests/` altında xUnit ile yazılmış birim testleri var
(şifre hash'leme ve zamanlama/çakışma algoritması için). Çalıştırmak için:

```bash
cd GsuTimetablingSystem.Tests
dotnet test
```

Not: Test projesi ana projeye referans verdiğinden, ilk çalıştırmada ana projenin
`BuildFrontend` MSBuild target'ı (npm install + npm run build) da tetiklenir —
bu, `dotnet build`/`dotnet run` ile aynı, zaten var olan davranıştır.

## Ortam Değişkenleri

| Değişken | Açıklama | Varsayılan |
|---|---|---|
| `GSU_MYSQL_CONNECTION_STRING` | Tam bağlantı dizesi verilirse diğer MySQL değişkenlerini geçersiz kılar | — |
| `GSU_MYSQL_USER` | MySQL kullanıcı adı | `root` |
| `GSU_MYSQL_PASSWORD` | MySQL şifresi | *(boş)* |
| `GSU_ADMIN_NAME` / `GSU_ADMIN_EMAIL` / `GSU_ADMIN_PASSWORD` | Admin hesabı bilgileri | `appsettings.json`'daki değerler |

Bağlantı bilgilerinin varsayılanları `appsettings.json` içindedir.

## Demo Hesaplar

| Rol | Kullanıcı adı | Şifre |
|---|---|---|
| Admin | `admin@gsu.edu.tr` | `1234` |
| Öğretmen | `1001` (öğretmen no) | `1234` |
| Öğrenci | `2022001` | `19/07/2004` |

Tüm öğretmen ve öğrenci giriş bilgileri `ogretmen_giris_bilgileri.xlsx` ve
`ogrenci_giris_bilgileri.xlsx` dosyalarında listelidir. Şifreler veritabanında
PBKDF2 ile hash'lenmiş olarak saklanır; yukarıdaki tabloda görünen değerler
kullanıcıların giriş sırasında yazdığı gerçek (düz metin) şifrelerdir, veritabanı
içeriği değildir.

## Proje Yapısı

```
GsuTimetablingSystem/
├── Program.cs                       # API uç noktaları, oturum/yetkilendirme
├── Data/
│   ├── MySqlScheduleRepository.cs   # Şema, seed verisi, tüm SQL sorguları
│   └── PasswordHasher.cs            # PBKDF2 şifre hash'leme
├── Services/
│   └── BacktrackingSchedulerService.cs  # Zamanlama (backtracking) algoritması
├── Models/                          # DTO/entity sınıfları
├── GsuTimetablingSystem.Tests/      # xUnit birim testleri
├── frontend/
│   ├── src/App.tsx, main.tsx        # Vite giriş noktası
│   └── components/                  # AdminPanel, TeacherPanel, StudentPanel, Timetable
└── wwwroot/                         # Derlenmiş frontend çıktısı (elle düzenlenmez)
```

## Bilinen Kapsam Sınırları

Ayrıntılı ve önceliklendirilmiş bir değerlendirme için `GSU_Teslim_Degerlendirme_Raporu.docx`
dosyasına bakın. Özetle:

- Oda / öğretmen / zaman dilimi ekleme-silme arayüzden yapılamıyor (yalnızca seed verisi).
- Tüm yarıyıllardaki (1-8) ders-öğretmen eşleşmeleri ve ön koşullar ects.gsu.edu.tr üzerindeki
  resmi müfredat ve ders detay sayfalarıyla (11.07.2026 itibariyle) birebir doğrulandı.
- Admin şifresi `appsettings.json`'da düz metin tutuluyor (tek operatör sırrı; öğrenci/öğretmen
  şifreleri veritabanında hash'lenmiş durumda).
