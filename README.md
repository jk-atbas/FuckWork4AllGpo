# FuckWork4AllGpo

Windows-Dienst (via NSSM), der die Installation von **work4all** über GPOs automatisch erkennt, blockiert und entfernt.

## Was macht der Dienst?

> Angedacht unter einem richtigen Nutzer ausgeführt zu werden

### 1. Echtzeit-Überwachung (FileSystemWatcher)
- Überwacht `%LocalAppData%` auf die Erstellung von `work4all GmbH`-Ordnern
- Überwacht das Desktop-Verzeichnis (inkl. Public Desktop) auf neue `.lnk`-Verknüpfungen
- Bei Erkennung: Sofortige Löschung (nach kurzem Delay für Dateifreigabe)
- Debouncing verhindert Mehrfachreaktionen auf denselben Event

### 2. Periodische Bereinigung (alle 4 Stunden)
- Durchsucht das Benutzerprofil nach `%LocalAppData%\work4all GmbH\work4all\*`
- Beendet laufende `work4all.exe`-Prozesse
- Löscht den kompletten `work4all GmbH`-Ordner inkl. aller Unterverzeichnisse
- Entfernt Desktop-Verknüpfungen (nach Name und Ziel-Analyse der .lnk-Dateien)
- Retry-Logik bei gesperrten Dateien (5 Versuche, 3s Pause)

### 3. Logging
- Tägliche Logdateien unter `<Installationsverzeichnis>/logs/`
- 30 Tage Aufbewahrung
- Konsolen-Ausgabe (sichtbar bei manuellem Start)

## Voraussetzungen

- **.NET 10 SDK** zum Kompilieren (oder fertige Binaries verwenden)
- **NSSM** ([nssm.cc](https://nssm.cc)) zur Dienst-Registrierung
- **Administrator-Rechte** (der Dienst muss als SYSTEM oder Admin laufen)

## Kompilieren

```powershell
cd FuckWork4AllGpo

# Self-contained Build (keine .NET Runtime auf dem Zielrechner nötig)
dotnet publish -c Release -r win-x64 --self-contained true -o ./publish
```

Die fertige Anwendung liegt dann unter `./publish/FuckWork4AllGpo.exe`.

## Installation

### Option A: Install-Skript (empfohlen)

1. NSSM herunterladen und in den PATH legen
2. Den `publish`-Ordner auf den Zielrechner kopieren
3. `install.bat` als Administrator ausführen

### Option B: Manuell mit NSSM

```powershell
# Service installieren
nssm install Work4allBlocker "C:\Pfad\zu\publish\FuckWork4AllGpo.exe"

# Konfigurieren
nssm set Work4allBlocker DisplayName "Work4all Blocker Service"
nssm set Work4allBlocker Description "Blocks and removes work4all GPO installations"
nssm set Work4allBlocker Start SERVICE_AUTO_START
nssm set Work4allBlocker AppDirectory "C:\Pfad\zu\publish"

# Neustart bei Absturz
nssm set Work4allBlocker AppExit Default Restart
nssm set Work4allBlocker AppRestartDelay 10000

# Starten
nssm start FuckWork4AllGpo
```

## Deinstallation

```powershell
nssm stop FuckWork4AllGpo
nssm remove FuckWork4AllGpo confirm
```

Oder `uninstall.bat` als Administrator ausführen.

## Konfiguration

Aktuell sind die Parameter im Code fest definiert:

| Parameter | Wert | Datei |
|-----------|------|-------|
| Cleanup-Intervall | 4 Stunden | `CleanUpWorker.cs` |
| Retry-Versuche | 5 | `CleanUpWorker.cs` |
| Retry-Pause | 3 Sekunden | `CleanUpWorker.cs` |
| Debounce-Intervall | 10 Sekunden | `FileSystemWatcherWorker.cs` |
| Lösch-Delay | 5 Sekunden | `FileSystemWatcherWorker.cs` |
| Log-Aufbewahrung | 30 Tage | `Program.cs` |

## Projektstruktur

```
FuckWork4AllGpo/
├── Program.cs                          # Entry Point, Host-Konfiguration, Serilog
├── FuckWork4AllGpo.csproj              # Projektdatei (.NET 8, self-contained)
├── Services/
│   ├── Work4allLocator.cs              # Findet work4all-Pfade und Shortcuts
│   ├── CleanUpWorker.cs                # Periodische Bereinigung (4h)
│   └── FileSystemWatcherWorker.cs      # Echtzeit-Erkennung via FSW
├── install.bat                         # NSSM-Installationsskript
├── uninstall.bat                       # NSSM-Deinstallationsskript
└── README.md                           # Diese Datei
```

## Fehlerbehebung

- **Dienst startet nicht**: Logdateien unter `logs/` prüfen. Läuft der Dienst als SYSTEM?
- **Dateien werden nicht gelöscht**: Prüfen ob work4all.exe noch läuft (`tasklist | findstr work4all`)
- **Verknüpfungen tauchen wieder auf**: Die GPO liefert sie ggf. bei jedem GP-Update erneut aus. Der Dienst entfernt sie beim nächsten Zyklus oder sofort per FileSystemWatcher.
- **FileSystemWatcher reagiert nicht**: Manchmal verliert der FSW Events bei hoher Last. Die periodische Bereinigung fängt das auf.

## Hinweise

- Der Dienst sollte als **Local System** laufen, damit er Zugriff auf alle Benutzerprofile hat.
- GPOs werden typischerweise bei Anmeldung und alle 90 Minuten angewendet. Das 4h-Intervall + Echtzeit-Watcher deckt das zuverlässig ab.
- Bei Bedarf kann das Cleanup-Intervall in `CleanUpWorker.cs` angepasst werden.
