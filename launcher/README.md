# ExamLock — imtahan proqramı (exe)

Tələbənin açdığı `Imtahan-*.exe` faylının kodu. Windows Forms + WebView2, .NET 10.

Bu kod serverdəki `ExamLock.exe`-dən bərpa edilib (decompile), çünki ilkin mənbə kodu itmişdi.

## Yığmaq

```powershell
cd launcher
dotnet publish -c Release
```

Nəticə: `launcher\bin\Release\net10.0-windows\win-x64\publish\ExamLock.exe`

## Serverə yükləmək

Faylı serverdə `/srv/examlock/bin/ExamLock.exe` ilə əvəz edin (köhnəsinin ehtiyat nüsxəsini saxlayın).
Server hər imtahan üçün bu faylın sonuna şifrələnmiş konfiqurasiya əlavə edib tələbəyə verir
(`src/server.js` → `buildOverlay`, burada `ExamLock/ConfigReader.cs`).
Servisi yenidən başlatmaq lazım deyil.

## Əsas fayllar

- `ExamLock/ExamForm.cs` — imtahan pəncərəsi, icazəli saytlar, qadağan keçidin bloklanması və bərpası
- `ExamLock/ProcessGuard.cs` — imtahan vaxtı bağlanan proqramlar
- `ExamLock/KeyboardBlocker.cs`, `PolicyGuard.cs` — klaviatura və sistem məhdudiyyətləri
- `ExamLock/Api.cs` — serverlə əlaqə (`/api/client/*`)
