# Timekeeper

Håller koll på vad du gör vid datorn under arbetsdagen och föreslår en tidslogg per kund i slutet av dagen.

- **Aktivitet:** aktivt fönster, webbadress i Edge/Chrome, sökväg till öppna Office-dokument (även SharePoint), skärmdumpar med lokal textigenkänning. Allt sparas bara på datorn, i `%LOCALAPPDATA%\Timekeeper`.
- **Tidslogg:** kundnamn i fönstertitlar känns igen direkt. Resten klassas av Jev (TypeSafe AI), och när Jev är osäker tittar Luna (OpenAI GPT-6 Luna) på skärmdumpen. Luna skriver också kommentarerna. Raderna kan justeras och kopieras till Blikk.
- **Kunder:** kundregister med sökord som kortnamn, organisationsnummer eller mappnamn.

## Installera

Ladda ner `Timekeeper-win-Setup.exe` från senaste [release](https://github.com/sliarsgard/timekeeper/releases/latest) och kör den. Appen söker sedan själv efter nya versioner och visar **Starta om och uppdatera** när en finns.

## Släppa en ny version

```powershell
./scripts/release.ps1          # nästa patchversion
./scripts/release.ps1 0.2.0    # eller en angiven version
```

Skriptet taggar och pushar. GitHub Actions (`.github/workflows/release.yml`) kör testerna, bygger och publicerar releasen med [Velopack](https://velopack.io).

## Utveckling

```powershell
dotnet test
dotnet run --project src/Timekeeper.App
```

Sätt `TIMEKEEPER_DATA_DIR` för att köra mot en annan datamapp än den riktiga, t.ex. för att prova en ändring medan den installerade appen är igång.

| Projekt | Innehåll |
|---|---|
| `Timekeeper.Core` | Segmentering av aktivitet, kundmatchning och bygget av tidsloggen |
| `Timekeeper.Windows` | Insamling: fönster, inaktivitet, webbadresser, Office, skärmdumpar, OCR |
| `Timekeeper.Data` | SQLite-lagring |
| `Timekeeper.Ai` | Klienter för Jev och Luna |
| `Timekeeper.App` | Avalonia-gränssnittet, inställningar och uppdateringar |
