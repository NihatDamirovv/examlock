# ExamLock — layihə konteksti və görülən işlər

Bu sənəd layihəni başqa bir AI-yə və ya tərtibatçıya təhvil vermək üçündür.
Oxuyan burada bütün quruluşu, serveri, imtahan proqramını (exe) və 2026-11-25
tarixində edilən dəyişiklikləri tapır. Dil: izahlar Azərbaycanca, kod ingiliscə.

---

## 1. ExamLock nədir

ExamLock imtahan zamanı tələbənin kompüterini "kilidləyən" sistemdir (Safe Exam
Browser məntiqi). İki hissədən ibarətdir:

1. **Server** (`src/server.js`) — müəllim/admin veb paneli + imtahan proqramının
   (exe) danışdığı API. Node.js + Express + `node:sqlite`, əlavə paket yoxdur.
2. **İmtahan proqramı** (`launcher/`) — tələbənin açdığı `Imtahan-*.exe`.
   Windows Forms + WebView2, .NET 10. İmtahanı tam ekran, kilidli brauzerdə açır,
   qadağan saytları və proqramları bloklayır, pozuntuları serverə göndərir.

### İş axını
1. Müəllim panelə girir, imtahan yaradır (ad + imtahan saytının linki, məs. Google Forms).
2. "Faylı yüklə (.exe)" ilə həmin imtahana aid exe yüklənir. Server exe-nin sonuna
   şifrələnmiş konfiqurasiya (server ünvanı + imtahan kodu + açar) əlavə edir.
3. Müəllim exe-ni (və ya `/e/KOD` linkini) tələbələrə göndərir.
4. Tələbə exe-ni açır, ad-soyadını yazır, "İmtahana başla" basır. Brauzer kilidli
   rejimdə imtahan saytını açır. İmtahan linki tələbəyə heç vaxt göstərilmir
   (`/go/<token>` bir dəfəlik keçid).
5. Tələbə bitirib "Çıxış" edir (şifrə varsa yazır). Sonra məcburi "Sil" ekranı gəlir.

---

## 2. Server

- **Yer:** serverdə `/srv/examlock`, `systemd` servisi `examlock.service`
  (istifadəçi `examlock`, `ExecStart=node src/server.js`).
- **Daxili port:** `9084`. Caddy `http://13.49.1.59:8084` → `127.0.0.1:9084`
  (`/etc/caddy/Caddyfile`). Serverdə başqa saytlar da var (rentacar, XYpos,
  arifmetik və s.) — ExamLock-a yalnız `:8084` aiddir.
- **Baza:** SQLite, `/srv/examlock/data/examlock.db`. Cədvəllər: `exams`,
  `sessions`, `go_tokens`, `events`, və **yeni:** `teachers`.
- **Admin şifrəsi:** `/etc/examlock/examlock.env` içində `ADMIN_PASSWORD`
  (koda yazılmır, repoya düşmür).
- **Repo (GitHub):** https://github.com/NihatDamirovv/examlock
  `db/`, `data/`, `.env`, `*.pem`, `bin/*.exe` və build qovluqları `.gitignore`-dadır.

### Səhifələr və rollar
- `/login` — giriş. Müəllim adları kart kimi görünür; adını seçib şifrə yazır.
  Altda ayrıca "Administrator girişi".
- `/admin` — yalnız administrator (`ADMIN_PASSWORD`). Müəllim idarəsi.
- `/panel` — müəllim paneli. Hər müəllim **yalnız öz** imtahanlarını görür.
- `/e/:code`, `/go/:t`, `/api/client/*` — tələbə/exe tərəfi. **Dəyişməyib.**

### Mühüm server API-ləri
- Auth: `POST /api/login/admin`, `POST /api/login/teacher`, `POST /api/logout`,
  `GET /api/teachers` (login səhifəsi üçün, yalnız şifrəsi olan + aktiv müəllimlər).
- Admin: `GET/POST/PUT/DELETE /admin/api/teachers...`, `GET /admin/api/exams`,
  `PUT /admin/api/exams/:id/owner` (imtahanın sahibini dəyişir).
- Müəllim: `GET/POST/PUT/DELETE /panel/api/exams...`,
  `GET /panel/api/exams/:id/sessions` (canlı izləmə),
  `POST /panel/api/sessions/:id/reentry`, `GET /panel/download/:id`.

### Təcridetmə (vacib)
Bütün müəllim sorğuları `teacher_id` ilə süzülür (`examsByTeacher`, `examOfTeacher`,
`sessionOfTeacher`). Bir müəllim başqasının imtahanını görə, dəyişə, silə və ya
exe-sini yükləyə bilmir. Admin cookie müəllim API-sinə, müəllim cookie admin
API-sinə keçmir. CSRF üçün yazı sorğuları `X-Requested-With: fetch` tələb edir.

---

## 3. İmtahan proqramı (exe) — `launcher/`

- **Texnologiya:** .NET 10, Windows Forms, WebView2 (`Microsoft.Edge WebView2 Runtime`
  tələb olunur). Tək fayllı, self-contained publish.
- **Qeyd:** bu C# kodu mövcud `ExamLock.exe`-dən **decompile** ilə bərpa edilib,
  çünki ilkin mənbə kodu itmişdi. ILSpy (`ilspycmd`) ilə.
- **Yığmaq:**
  ```powershell
  cd launcher
  dotnet publish -c Release
  ```
  Nəticə: `launcher/bin/Release/net10.0-windows/win-x64/publish/ExamLock.exe`
- **Serverə yükləmək:** həmin faylı `/srv/examlock/bin/ExamLock.exe` ilə əvəz et
  (köhnəsinin backup-ını saxla). Servisi restart etmək lazım deyil — server faylı
  hər yükləmədə oxuyur. Overlay (konfiqurasiya) `src/server.js` → `buildOverlay`,
  oxunması `launcher/ExamLock/ConfigReader.cs` (açar hər ikisində eyni olmalıdır).

### Əsas fayllar
- `ExamForm.cs` — imtahan pəncərəsi: icazəli saytlar, qadağan keçidin bloklanması,
  "Geri" düyməsi, canlı nəzarət, heartbeat.
- `StartForm.cs` — ad-soyad ekranı, "İmtahana başla".
- `DeleteForm.cs` — **yeni** məcburi "Sil" ekranı.
- `SelfDelete.cs` — **yeni** exe-nin yığışdırılması.
- `ProcessGuard.cs` — imtahan vaxtı bağlanan proqramlar (brauzerlər, messencerlər,
  ekran yazanlar, AI alətləri və s.).
- `KeyboardBlocker.cs`, `PolicyGuard.cs` — klaviatura və sistem məhdudiyyətləri.
- `Api.cs` — serverlə əlaqə (`/api/client/*`).

---

## 4. 2026-11-25 tarixində edilən dəyişikliklər

### A. Rollar: admin + ayrı müəllim panelləri (server)
- Yeni `teachers` cədvəli. İlk işə salınmada iki müəllim yaradılır:
  **Sabina Nağıyeva** və **Dilarə Quluzadə** (şifrəsiz — admin təyin edir).
- `exams` cədvəlinə `teacher_id` sütunu əlavə olundu.
- Yeni giriş səhifəsi (`public/login.html` + `login.js`): müəllim kartları + admin girişi.
- Admin paneli (`public/admin.html` + `admin.js`): müəllim əlavə et / şifrə təyin et /
  bağla / sil; bütün imtahanlara baxış və sahibini dəyişmək.
- Müəllim paneli köhnə paneldən adlandırıldı (`teacher.html` + `teacher.js`), bütün
  API-ləri `teacher_id` ilə süzülür.
- Köhnə imtahan ("Rəqəmsal kriminalistikanın əsasları") Sabina Nağıyevaya verildi.
- Müəllimin şifrəsi dəyişəndə / girişi bağlananda açıq sessiyası dərhal etibarsız olur.

### B. Qadağan saytdan sonra qara ekran düzəldildi (exe)
- Problem: imtahan saytı başqa domenə (məs. `accounts.google.com`) yönləndirəndə
  (redirect) keçid bloklanırdı, amma əvvəlki səhifə bağlanmış olurdu → ekran boş qalırdı.
- Həll (`ExamForm.cs`): proqram son icazəli imtahan səhifəsini yadda saxlayır. Qadağan
  keçid bloklananda səhifəni yoxlayır: sağlamdırsa toxunmur (cavablar itməsin);
  boşdursa əvvəl "Geri", sonra son icazəli səhifəni açır. 30 saniyədə ən çox 3 cəhd.
- Domen məntiqi (`IsAllowed`): yalnız imtahan domeninin özü və alt domenləri (www, m.)
  açılır; bütün səhifələr/endpoint-lər/API-lər açıq; başqa domen bağlı. Saxta domenlər
  (`youtube.com.evil.com`) bloklanır. 15 test halı ilə yoxlanıb.

### C. Sol yuxarıda "← Geri" düyməsi (exe)
- `ExamForm.cs`: yalnız imtahan saytının öz səhifələri arasında işləyir (qadağan
  saytlar tarixçəyə düşmür). Geri getmək mümkün deyilsə düymə sönükdür.

### D. Çıxışdan sonra məcburi "Sil" ekranı + exe-nin yığışdırılması (exe)
- `DeleteForm.cs`: çıxışdan (şifrə) sonra gələn məcburi ekran. Yalnız "Sil" düyməsi,
  X / Alt+F4 / Escape işləmir.
- `SelfDelete.cs`: "Sil" basılanda exe öz adını eyni qovluqda gizli `.~exlkdel_*.tmp`
  adına dəyişir → görünən `Imtahan-*.exe` dərhal Yükləmələrdən yox olur. Gizli qalıq
  növbəti dəfə istənilən imtahan exe-si açılanda silinir (`CleanupLeftovers`,
  program başlayanda).

#### Niyə "dərhal hər yerdən fiziki silmə" YOX
Windows işləyən proqramın öz faylını silməyə icazə vermir (fayl kilidli). Yoxlanıb:
- birbaşa silmə / Zibil qutusu → "Access denied";
- cmd ilə arxa planda silmə → Windows Defender bloklayır (bu səbəbdən köhnə üsul silmirdi);
- "yenidən başlayanda sil" (`MoveFileEx` reboot) → admin icazəsi istəyir, tələbədə yoxdur.
Tək işləyən yol: adını dəyişib gizlətmək. Əvvəlki aqressiv süpürmə (Recycle Bin /
Recent / başqa qovluqlar) **çıxarıldı**, çünki işləmirdi və exe-ni Defender-in
gözündə virusa oxşadıb karantin riski yaradırdı. **Təhlükəsizlik serverdədir:**
qalan fayl ilə təkrar imtahan vermək mümkün deyil (`finishedOnMachine` — hər
kompüterdən bir dəfə; təkrar giriş üçün müəllim panelindən icazə verməlidir).

---

## 5. Deploy qaydası (xülasə)

**Server kodu (`src/`, `public/`):**
```bash
# kompüterdə, repo qovluğunda:
git archive HEAD src public > /tmp/el.tar
scp -i nihatkeypair.pem /tmp/el.tar ubuntu@13.49.1.59:/tmp/
# serverdə:
sudo systemctl stop examlock
sudo cp -a /srv/examlock/{src,public,data} /srv/examlock-backup-$(date +%F-%H%M%S)/
sudo rm -rf /srv/examlock/public && sudo tar xf /tmp/el.tar -C /srv/examlock
sudo chown -R examlock:examlock /srv/examlock/src /srv/examlock/public
sudo systemctl start examlock
```

**İmtahan proqramı (`launcher/`):**
```powershell
cd launcher; dotnet publish -c Release
```
```bash
scp -i nihatkeypair.pem <publish>/ExamLock.exe ubuntu@13.49.1.59:/tmp/ExamLock.new.exe
# serverdə:
sudo cp -a /srv/examlock/bin/ExamLock.exe /srv/examlock/bin/ExamLock.exe.bak-$(date +%F-%H%M%S)
sudo install -o examlock -g examlock -m 755 /tmp/ExamLock.new.exe /srv/examlock/bin/ExamLock.exe
```
Restart lazım deyil. Artıq yüklənmiş köhnə exe-lər dəyişmir — tələbə yenidən yükləməlidir.

**Serverə giriş:** `ssh -i nihatkeypair.pem ubuntu@13.49.1.59` (keypair `Downloads/nihatkeypair.pem`).

---

## 6. İstifadəçinin etməli olduğu şeylər
1. `13.49.1.59:8084` → "Administrator girişi" → Sabina və Dilarəyə şifrə təyin et
   (şifrəsiz müəllim giriş səhifəsində görünmür).
2. Yeni funksiyalar üçün tələbələr exe-ni **yenidən** yükləməlidir (köhnə exe-lərdə
   yeni düzəlişlər yoxdur).

## 7. Qeydlər / məhdudiyyətlər
- Brauzerin öz yükləmə tarixçəsindən (Ctrl+J) faylın adı silinmir, amma fayl özü artıq
  işləmir/gizlənir.
- Admin şifrəsi serverdəki `.env`-dədir; dəyişdirmək üçün orada dəyiş + `systemctl restart examlock`.
- Backuplar serverdə: `/srv/examlock-backup-*` və `/srv/examlock/bin/ExamLock.exe.bak-*`.
