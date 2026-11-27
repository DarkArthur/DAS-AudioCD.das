# DiscCheck

Applicazione desktop Windows 10/11 x64 per il controllo qualità dei CD audio masterizzati (verifica bit-perfect, scansione C1/C2/CU, archivio SQLite).

## Stack
- C# (LangVersion predefinita dell'SDK), .NET 11 (SDK 11.0.x, `global.json` con `rollForward` = `latestFeature`), nessuna funzionalità preview.
- Avalonia 11, CommunityToolkit.Mvvm, Microsoft.Extensions.Hosting, Serilog.
- SQLite con Microsoft.Data.Sqlite + Dapper (niente EF).
- ScottPlot.Avalonia 5.x, solo in `DAS.Design.Charts`.
- Test: xUnit, FluentAssertions, NSubstitute, FsCheck.Xunit.

## Struttura
- `src/DiscCheck.App`: Avalonia, ViewModel, DI. Referenzia tutti gli altri progetti.
- `src/DiscCheck.Core`: dominio, interfacce, logica pura. Nessun riferimento a progetti.
- `src/DiscCheck.Scsi`: `IScsiTransport`, MMC, WinScsiPassThrough (-> Core).
- `src/DiscCheck.Scanners`: scanner di qualità (-> Core, Scsi).
- `src/DiscCheck.Verify`: verifica bit-perfect (-> Core, Scsi).
- `src/DiscCheck.Data`: SQLite, migrazioni, repository (-> Core).
- `src/DAS.Design`: token, stili, icone; solo Avalonia 11, nessun `DiscCheck.*`.
- `src/DAS.Design.Charts`: grafici (Styling, Interaction, Formatting, Export, Models); solo `DAS.Design` e ScottPlot.Avalonia 5.x, nessun `DiscCheck.*`. Changelog in `CHANGELOG.md`.
- `tests/`: `DiscCheck.Core.Tests`, `DiscCheck.Scsi.Tests`, `DiscCheck.Hardware.Tests` (Category=Hardware), `DAS.Design.Tests`, `DAS.Design.Charts.Tests`, `Fixtures/`.

## Regole
- Tutto l'hardware passa da `IScsiTransport`.
- I ViewModel orchestrano soltanto; la logica di dominio sta in funzioni pure nel Core.
- Colori e temi solo tramite Design_Token `Das.*`.
- I grafici si mostrano solo con `DasChartView`.
- I test che richiedono un drive hanno `Category=Hardware`.
- I test di prestazione (sensibili ai tempi) hanno `Category=Performance` e sono esclusi dal filtro standard.
- Non usare funzionalità preview o sperimentali.

## Comandi
- `dotnet build`
- `dotnet test --filter "Category!=Hardware&Category!=Performance"`
- `dotnet test tests/DAS.Design.Charts.Tests --filter "Category=Performance"` (prestazioni dei grafici; `DAS_PERF_TOLERANCE` moltiplica le soglie, default 1)
- `dotnet run --project src/DiscCheck.App`
