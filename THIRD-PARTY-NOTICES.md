# Licenze e distribuzione

DiscCheck è distribuito con licenza **GNU General Public License v2.0 or later** (file `LICENSE`).
Copyright (C) 2026 DarkArthur DAS.

Decisione D2 (`docs/decisions.md`): opzione (a), distribuzione con GPL-2.0, dal 2026-11-27.

## Codice portato da QPxTool

`src/DiscCheck.Scanners/LiteOnQscanCmd.cs` e `src/DiscCheck.Scanners/MmcSeekPort.cs` sono porting in C# di codice di QPxTool (`github.com/speed47/qpxtool`, commit `9c6a3fdb5e27`), licenza "GPL v2 or later". Ogni file mantiene l'intestazione originale (file, copyright, licenza) e una nota di modifica (Req. 25.7). Il testo della GPL-2.0 è nel file `LICENSE` ed è incluso nell'installer e nello zip delle release (Req. 25.8). Il sorgente completo è nel repository pubblico.

## Componenti di terze parti distribuiti con l'app

| Componente | Licenza | Testo |
|------------|---------|-------|
| QPxTool (porting dei comandi Lite-On) | GPL-2.0-or-later | `LICENSE` |
| CUETools.Codecs.FLAKE 1.0.5 (decoder FLAC, D1) | LGPL-3.0, assembly separato collegato dinamicamente e sostituibile | `licenses/LGPL-3.0.txt`, `licenses/GPL-3.0.txt` |
| Font Inter | SIL Open Font License 1.1 | `src/DAS.Design/Assets/Fonts/OFL.txt` |
| Logo e icone DAS.Design | CC-BY 4.0, titolare DarkArthur DAS | `src/DAS.Design/LICENSE-ASSETS.md` |
| Avalonia, ScottPlot, Dapper, Serilog, Microsoft.Data.Sqlite, .NET | MIT / Apache-2.0 (licenze dei rispettivi pacchetti NuGet) | nei rispettivi pacchetti |
