# Implementation Plan: DiscCheck

## Overview

Il piano segue le milestone M0-M8 del Requirement 27. Ogni milestone termina con un checkpoint. Il linguaggio di implementazione è C# 12 su .NET 8 (come nel design). I test usano xUnit, FluentAssertions e NSubstitute; i property test usano FsCheck.Xunit con almeno 100 iterazioni e un commento `// Feature: disccheck, Property N: <testo>`.

Regole trasversali:
- Tutto l'hardware passa da `IScsiTransport`. I ViewModel dipendono solo da interfacce del Core.
- I CDB vendor Lite-On si portano esclusivamente da QPxTool_Source (`plugins/liteon/qscan_cmd.cpp`, `qscan_plugin.cpp`). Non si deducono né si inventano mai.
- Le decisioni D1, D2 e D5 bloccano rispettivamente M5, M6 e la pubblicazione (Requirement 25.2, 25.3, 25.6, 36.3, 36.4). Se una decisione è "aperta" quando il task relativo la richiede, il task chiede all'utente la scelta e non procede finché non è risolta.
- I sotto-task di test marcati con `*` sono opzionali. I test di `DiscCheck.Hardware.Tests` portano `[Trait("Category","Hardware")]`.

## Tasks

- [ ] 1. M0: Soluzione, infrastruttura, DAS.Design e CLAUDE.md
  - [ ] 1.1 Creare la soluzione e la struttura dei progetti
    - Creare `DiscCheck.sln` con `src/DiscCheck.App`, `DiscCheck.Core`, `DiscCheck.Scsi`, `DiscCheck.Scanners`, `DiscCheck.Verify`, `DiscCheck.Data`, `src/DAS.Design` e `tests/DiscCheck.Core.Tests`, `DiscCheck.Scsi.Tests`, `DiscCheck.Hardware.Tests`, `DAS.Design.Tests`
    - Impostare i riferimenti come da design (Core senza dipendenze; DAS.Design solo Avalonia 11) e i pacchetti (Avalonia 11, CommunityToolkit.Mvvm, ScottPlot.Avalonia 5.x, Microsoft.Data.Sqlite, Dapper, Microsoft.Extensions.Hosting, Serilog, xUnit, FluentAssertions, NSubstitute, FsCheck.Xunit)
    - Creare `CLAUDE.md` nella radice con stack, struttura, regole e i comandi `dotnet build`, `dotnet test --filter "Category!=Hardware"`, `dotnet run --project src/DiscCheck.App`
    - Creare la cartella `tests/Fixtures`
    - _Requirements: 1.1, 1.2, 1.10, 23.1_

  - [ ] 1.2 Creare il registro delle decisioni D1-D5
    - Creare `docs/decisions.md` con una voce per D1, D2, D3, D4, D5: identificativo, opzioni, stato ("aperta"/"risolta"), opzione scelta, data AAAA-MM-GG, motivazione
    - Tutte le decisioni partono "aperta"; D5 con le opzioni (a), (b), (c) del Requirement 36.2
    - _Requirements: 25.1, 25.10, 36.2_

  - [ ] 1.3 Implementare Design_Token, DasTheme e DasDesignInfo in DAS.Design
    - Dizionari Light e Dark con i valori esatti del Requirement 29 (colori di marca, scala neutra N0-N900, superfici, stati, grafici, `Das.Color.Series.1..8`)
    - `Das.Brush.*`, tipografia (Inter incorporato con fallback, Cascadia Mono), dimensioni testo, `Das.Space.*`, `Das.Radius.*`
    - `DasTheme : Styles` (include FluentTheme), `AppBuilder.UseDasTheme()`, `DasDesignInfo.Version`, supporto Accent_Override
    - Creare `CHANGELOG.md` (voce 1.0.0), `LICENSE-ASSETS.md` con "licenza in definizione (D5)", `Assets/Fonts/OFL.txt`
    - _Requirements: 28.1-28.11, 29.1-29.15, 36.1, 36.3, 36.5, 36.7_

  - [ ] 1.4 Implementare stili e controlli condivisi di DAS.Design
    - Stili `Button` (primary, secondary, danger), DataGrid, `Border.card`, classi `TextBlock`, focus 2 px, disabilitato 40%
    - Controlli `DasIcon` (Name, Size 16/20/24/32, fallback `das.status.warning` con log), `DasStatusBadge` (icona + testo + colori), `DasLogo` (Form, Height, variante per tema)
    - _Requirements: 30.1-30.9, 31.7, 33.5_

  - [ ] 1.5 Creare gli asset del DAS_Logo
    - 8 SVG (`das-logo-*` e `das-mark-*` nelle varianti color, light, dark, mono) con tracciati vettoriali, `das.ico` con 7 dimensioni, `logo.sha256`
    - Esporre `Das.Logo.Lockup` e `Das.Logo.Mark` come risorse
    - _Requirements: 31.1-31.8, 32.1_

  - [ ] 1.6 Implementare l'host dell'App (DI, Serilog, finestra principale, splash)
    - `Host.CreateDefaultBuilder`, registrazione servizi, `LoggingLevelSwitch` (Information, Verbose in Modalità_Debug), log su file con fallback a console e avviso
    - `app.manifest` con `requireAdministrator`; avviso se non amministratore con scansioni e verifica disabilitate
    - Gestione errori di avvio (log, messaggio, exit code ≠ 0); `DasTheme` in `App.axaml` seguendo il tema di sistema
    - Finestra principale con lockup 32 px, nome app, separatore (Requirement 32.2, 32.5); splash con lockup 128 px `-color` (max 5 s); finestra vuota senza contenuto funzionale
    - _Requirements: 1.3, 1.4, 1.5, 1.8, 1.9, 2.7, 2.9, 26.7, 32.2, 32.5, 32.7, 35.1_

  - [ ] 1.7 Implementare `ContrastCalculator` nel Core
    - Luminanza relativa sRGB e Contrast_Ratio WCAG 2.1
    - _Requirements: 29.8, 29.9, 37.5_

  - [ ]* 1.8 Property test per il Contrast_Ratio
    - **Property 22: Calcolo del Contrast_Ratio**
    - **Validates: Requirements 29.8, 29.9, 37.5**

  - [ ]* 1.9 Scrivere i test di DAS.Design (M0)
    - Parità chiavi Light/Dark, contrasto delle coppie dei Requirement 29/30 letto dai token, indipendenza da `DiscCheck.*` e build, caricamento headless di `DasTheme`, istanza di `DasIcon` e `DasStatusBadge` in entrambe le varianti, hash del logo, `DasDesignInfo.Version` = ultima voce di `CHANGELOG.md`
    - _Requirements: 37.5, 37.6, 37.7, 37.8, 37.9, 37.10, 37.11_

  - [ ]* 1.10 Scrivere i test di analisi statica dei file `.axaml`
    - In `DiscCheck.Core.Tests`: ogni Token_Key `Das.*`/`DiscCheck.*` esiste; nessun colore, `SolidColorBrush` o `FontFamily` letterale; errore con file, riga e chiave
    - _Requirements: 35.2, 35.3, 37.1, 37.2_

- [ ] 2. Checkpoint M0
  - Ensure all tests pass, ask the user if questions arise.
  - Verificare le condizioni M0 del Requirement 27.1.

- [ ] 3. M1: Trasporto SCSI, INQUIRY/MODE SENSE, enumerazione Drive
  - [ ] 3.1 Definire i tipi SCSI e `FakeScsiTransport`
    - `DataDirection`, `SenseData`, `ScsiResult` con `TransportError` (Timeout, WinError, OpenFailed), `IScsiTransport`
    - `FakeScsiTransport` con risposte da `tests/Fixtures/*.bin`
    - _Requirements: 2.1, 2.2, 2.8, 23.3_

  - [ ] 3.2 Implementare `WinScsiPassThrough`
    - `CreateFile`, `DeviceIoControl` con `IOCTL_SCSI_PASS_THROUGH_DIRECT`, sense da 32 byte, `IOCTL_SCSI_GET_CAPABILITIES` per `AlignmentMask`, buffer con `NativeMemory.AlignedAlloc`
    - Timeout 30 s (1-600 s; 60 s per l'avvio scansione), nessun retry, errori come valori in `ScsiResult`, log Verbose in Modalità_Debug
    - _Requirements: 2.3-2.6, 2.8-2.14_

  - [ ] 3.3 Implementare parser puri e `MmcCommands`
    - INQUIRY, MODE SENSE(10) pagina 0x2A, GET CONFIGURATION, READ TOC/PMA/ATIP, READ CD, SET CD SPEED (con validazione argomento), TEST UNIT READY, START STOP UNIT
    - `MmcResult<T>` con `CommandError` e `ParseError`; parser Sense fixed; lunghezze minime documentate
    - _Requirements: 3.1-3.8_

  - [ ]* 3.4 Scrivere i test Scsi.Tests con fixture
    - Per ogni comando una fixture `.bin` valida e una risposta troncata; SET CD SPEED con 8x, 0, > 0xFFFE e 0xFFFF; sense non-GOOD senza parsing
    - _Requirements: 3.9, 23.3, 23.6_

  - [ ] 3.5 Implementare `DriveEnumerator`, vista Drive e "Reset drive"
    - Enumerazione `DriveType.CDRom`, INQUIRY, `DriveInfoEx`, stati "non identificabile", messaggio senza drive, messaggi per privilegi e apertura
    - Comando "Reset drive" (fine scansione + TEST UNIT READY), abilitazione se Test_Mode residuo, esito entro 10 s
    - Usare `DasIcon` `das.drive.optical` e `das.reset` come segnaposto semantico
    - _Requirements: 2.9, 2.12, 4.1-4.4, 4.6-4.10, 26.5, 26.6_

  - [ ]* 3.6 Scrivere i test unitari di enumerazione e Reset drive
    - Con NSubstitute/Fake: nessun drive, INQUIRY fallita, Reset riuscito, Reset fallito con messaggio
    - _Requirements: 4.6, 4.7, 4.9, 4.10_

  - [ ]* 3.7 Scrivere gli hardware test M1
    - `[Trait("Category","Hardware")]`: INQUIRY e MODE SENSE 0x2A su iHAS124 via ICY BOX (modello "iHAS124", vendor "LITE-ON")
    - Un comando vendor di QPxTool_Source (`cmd_cd_errc_init`, solo se il CDB è letto dal sorgente) via bridge, con esito "accettato"/"rifiutato" documentato in `docs/risks.md`
    - _Requirements: 4.4, 26.1, 27.2_

- [ ] 4. Checkpoint M1
  - Ensure all tests pass, ask the user if questions arise.
  - Verificare le condizioni M1 del Requirement 27.2 (hardware test inclusi).

- [ ] 5. M2: TOC, ATIP e Media ID
  - [ ] 5.1 Implementare modelli e logica TOC nel Core
    - `Toc`, `TocEntry`, `TocParser` (READ TOC formato 0), `TocSerializer`, `TocHasher` (SHA-256 su serializzazione canonica), durate mm:ss
    - _Requirements: 5.1, 5.2, 5.8, 5.11, 5.12_

  - [ ]* 5.2 Property test per il round-trip della TOC
    - **Property 7: Round-trip della TOC**
    - **Validates: Requirements 5.11**

  - [ ]* 5.3 Property test per `toc_hash`
    - **Property 8: Determinismo e sensibilità del toc_hash**
    - **Validates: Requirements 5.8**

  - [ ] 5.4 Implementare `AtipParser` e `AtipTable`
    - Parsing ATIP (READ TOC formato 4) in `AtipTime` mm:ss:ff, `Core/Resources/atip-manufacturers.json` (versione ≥ 1, voci con fonte pubblica, esclusi 97:24:01 e 97:34:23), lookup esatto, "produttore sconosciuto" su assenza o JSON non valido con log
    - _Requirements: 5.3-5.7, 5.14_

  - [ ] 5.5 Implementare la vista Disco
    - TOC con durate e totale settori entro 2 s, Media_ID e produttore, messaggi per disco assente/non CD audio/ATIP non disponibile, nessun dato residuo del disco precedente, rilevamento DVD/BD con messaggio e comandi disabilitati
    - Associazione `discs.media_id` ↔ `media` solo tramite interfaccia (persistenza in M4)
    - Icone `das.disc.cd` e `das.eject`
    - _Requirements: 5.2, 5.4, 5.5, 5.9, 5.10, 5.12, 5.13, 24.7, 24.8_

  - [ ]* 5.6 Scrivere i test unitari TOC/ATIP
    - Fixture valide e troncate, CD non CD-R, tabella ATIP assente/invalida/senza versione, voci escluse
    - _Requirements: 5.5, 5.7, 5.13, 5.14, 23.2_

- [ ] 6. Checkpoint M2
  - Ensure all tests pass, ask the user if questions arise.
  - Verificare le condizioni M2 del Requirement 27.3.

- [ ] 7. M3: StandardC2Scanner, pipeline, grafico live e Verdetto
  - [ ] 7.1 Implementare `C2BitCounter` e `SampleAggregator`
    - Popcount su 294 byte (esclusi block error byte e riempimento del formato 0x14), campioni da 75 settori più gruppo residuo 1-74, `SpeedX`
    - _Requirements: 7.5-7.8_

  - [ ]* 7.2 Property test per l'additività del conteggio C2
    - **Property 1: Additività del conteggio dei bit C2**
    - **Validates: Requirements 7.11**

  - [ ]* 7.3 Property test per l'equivalenza dei formati 0x12 e 0x14
    - **Property 2: Il conteggio C2 è invariante rispetto al formato con block error byte**
    - **Validates: Requirements 7.4, 7.5**

  - [ ] 7.4 Implementare `Evaluator` e `Thresholds`
    - Statistiche C2/CU totali, C1 medio e massimo sui soli non nulli; regole OK/WARN/KO; regola binaria per StandardC2; `null` senza campioni
    - _Requirements: 11.1-11.4, 11.6, 11.10_

  - [ ]* 7.5 Property test per la monotonia del Verdetto
    - **Property 3: Monotonia del Verdetto rispetto agli errori**
    - **Validates: Requirements 11.8**

  - [ ]* 7.6 Property test per la classificazione secondo le Soglie
    - **Property 4: Classificazione del Verdetto secondo le Soglie**
    - **Validates: Requirements 11.1, 11.2, 11.3, 11.4**

  - [ ]* 7.7 Property test per il Verdetto binario di StandardC2Scanner
    - **Property 5: Il Verdetto di StandardC2Scanner è binario**
    - **Validates: Requirements 11.6**

  - [ ]* 7.8 Property test per l'assenza di Verdetto senza campioni
    - **Property 6: Nessun Verdetto senza campioni**
    - **Validates: Requirements 11.10**

  - [ ] 7.9 Implementare `StandardC2Scanner`
    - `Probe` da MODE SENSE 0x2A (errore o risposta corta = non supportato con log), lettura sequenziale LBA 0 → lead-out, READ CD tipo 0x04, blocchi costanti da 26 o 27 settori, flag 0x12 o 0x14 costante, stop senza retry su errore con LBA/sense
    - _Requirements: 7.1-7.4, 7.6-7.8, 7.10_

  - [ ]* 7.10 Scrivere i test unitari di StandardC2Scanner
    - Con Fake_Transport: Probe non supportato, ultimo blocco residuo, errore READ CD, campioni conservati
    - _Requirements: 7.1, 7.2, 7.7, 7.10_

  - [ ] 7.11 Implementare `ScannerSelector`
    - Probe in ordine LiteOn → StandardC2 con timeout 5 s e cattura eccezioni, "forza standard", scanner aggiunti solo tramite DI
    - _Requirements: 6.1-6.7_

  - [ ] 7.12 Implementare `ScanPipeline`
    - Scan_Thread dedicato, `Channel` bounded 1.024 con `FullMode=Wait`, fan-out verso 2 consumer, `IProgress<ScanProgress>`, annullamento con chiusura Test_Mode, chiusura canale con errore dopo i campioni già pubblicati
    - _Requirements: 9.1-9.12, 10.8, 23.8, 23.9_

  - [ ]* 7.13 Property test per ordine e completezza dei consumer
    - **Property 12: Ordine e completezza dei consumer della pipeline**
    - **Validates: Requirements 9.5, 9.9, 9.10**

  - [ ]* 7.14 Property test per la backpressure senza scarto
    - **Property 13: Backpressure senza scarto**
    - **Validates: Requirements 9.4, 9.9**

  - [ ] 7.15 Implementare `ChartDownsampler`
    - Riduzione a ≤ larghezza px conservando il massimo per intervallo
    - _Requirements: 18.6_

  - [ ]* 7.16 Property test per il downsampling
    - **Property 21: Downsampling del grafico preserva i massimi**
    - **Validates: Requirements 18.6**

  - [ ] 7.17 Implementare la vista Scansione
    - Selettore scanner e velocità (4x, 8x preselezionata, 16x, 24x, massima), conferma per velocità non supportata, SET CD SPEED prima della lettura
    - Grafico ScottPlot live (barre C1/C2/CU, linea velocità con interruzione sui null), `ChartColorProvider` dai token `Das.Color.Chart.*`, riepilogo, `DasStatusBadge`, "n/d", note sui limiti e avviso di confronto
    - Stati Avvia/Annulla, messaggi di errore, Disco assente; icone `disccheck.scan.pulse`, `disccheck.chart.errors`
    - _Requirements: 6.2-6.6, 7.9, 10.1-10.7, 11.7, 18.1-18.9, 35.4, 35.6_

  - [ ]* 7.18 Scrivere i test unitari dei ViewModel di scansione
    - Macchina a stati (avvio, annulla, errore), Avvia disabilitato senza disco, velocità non supportata (conferma/annulla)
    - _Requirements: 10.6, 18.4, 18.5, 18.7, 18.8, 18.9_

- [ ] 8. Checkpoint M3
  - Ensure all tests pass, ask the user if questions arise.
  - Verificare le condizioni M3 del Requirement 27.4.

- [ ] 9. M4: SQLite, Repository e Storico
  - [ ] 9.1 Implementare `Migrator` e migrazione 001
    - Script SQL incorporati (schema del design con vincoli CHECK, UNIQUE, `WITHOUT ROWID`, `ON DELETE CASCADE`), `PRAGMA foreign_keys=ON`, transazione per script, rollback, rifiuto di `schema_version` superiore
    - _Requirements: 16.3-16.10, 16.13, 16.15_

  - [ ] 9.2 Implementare i Repository Dapper
    - `drives` (inserimento senza duplicati), `media`, `discs`, `scans` + `scan_samples` in un'unica transazione, `verifications` + `verification_tracks`, eliminazione scansione transazionale, interfacce nel Core
    - _Requirements: 4.5, 16.4, 16.5, 16.11, 16.12, 16.14, 17.6, 17.7_

  - [ ]* 9.3 Property test per il round-trip dei Repository
    - **Property 15: Round-trip dei Repository**
    - **Validates: Requirements 16.16**

  - [ ]* 9.4 Property test per la deduplicazione dei Drive
    - **Property 18: Idempotenza della deduplicazione dei Drive**
    - **Validates: Requirements 4.5**

  - [ ] 9.5 Collegare il salvataggio e implementare la vista Storico
    - Salvataggio della scansione completata (non se annullata), Database al percorso predefinito con errori di apertura, tre elenchi ordinati per data decrescente, filtri AND (supporto, velocità, date, Verdetto), riepilogo e grafico, tabella verifica, eliminazione con conferma, messaggi per elenco vuoto ed errori
    - _Requirements: 9.8, 16.1, 16.2, 16.10, 16.12, 17.1-17.9_

  - [ ]* 9.6 Scrivere i test unitari di Migrator e Storico
    - Script fallito con rollback, versione futura, vincoli CHECK, cascade, filtri senza risultati, tempo di salvataggio di 4.800 campioni
    - _Requirements: 16.4, 16.9, 16.11, 16.13, 16.15, 17.2, 17.8_

- [ ] 10. Checkpoint M4
  - Ensure all tests pass, ask the user if questions arise.
  - Verificare le condizioni M4 del Requirement 27.5.

- [ ] 11. M5: Cache, Cache_Defeat, verifica bit-perfect e offset
  - [ ] 11.1 Risolvere la decisione D1 (decoder FLAC)
    - Leggere `docs/decisions.md`; se D1 è "aperta", chiedere all'utente con `user_input` la scelta tra CUETools.Codecs.FLAKE (LGPL) e `flac.exe` esterno e registrare opzione, data e motivazione
    - Se D1 resta "aperta", non avviare gli altri task di M5 e segnalare D1 come bloccante nel registro
    - _Requirements: 25.2, 25.6_

  - [ ] 11.2 Implementare `IFlacDecoder` e il lettore WAV
    - Interfaccia nel Core, implementazione secondo D1 in un progetto non referenziato dal Verifier, decodifica WAV, controllo formato 16 bit / 44,1 kHz stereo
    - _Requirements: 14.5, 14.15, 14.16, 25.9_

  - [ ]* 11.3 Property test per il round-trip del decoder
    - **Property 9: Round-trip del decoder audio**
    - **Validates: Requirements 14.14**

  - [ ] 11.4 Implementare `CacheDetector` e `CacheDetectorRunner`
    - Logica con delegate di lettura/cronometro: 3 coppie T1/T2 su 27 settori, mediane, soglia 50%, stima `cache_kb` con aree 256-8.192 KB, salvataggio, interruzione con motivo su errore o disco < 28.500 settori, avviso per iHAS124 fuori da 1.536-2.560 KB
    - _Requirements: 12.1-12.5, 12.10_

  - [ ]* 11.5 Property test per la classificazione della cache
    - **Property 19: Classificazione della cache dalle mediane dei tempi**
    - **Validates: Requirements 12.2**

  - [ ] 11.6 Implementare `CacheDefeatPlanner`
    - Area ≥ max(2 × `cache_kb`, 4.096 KB), primo settore ≥ 28.500 settori dal blocco, 4.096 KB se `cache_kb` è NULL, limitazione con settori mancanti
    - _Requirements: 12.6-12.9_

  - [ ]* 11.7 Property test per la pianificazione del Cache_Defeat
    - **Property 20: Pianificazione del Cache_Defeat coerente con `cache_kb`**
    - **Validates: Requirements 12.6, 12.8**

  - [ ] 11.8 Implementare `OffsetDetector`, `PcmComparer` e `OffsetService`
    - Allineamento esatto entro ±5000 su 44.100 campioni centrali (traccia ≥ 54.100 campioni), confronto con esclusione di |k| campioni di bordo, validazione manuale ±5000, salvataggio `drives.read_offset`
    - _Requirements: 15.1-15.9_

  - [ ]* 11.9 Property test per il round-trip dell'offset
    - **Property 10: Round-trip del rilevamento dell'offset**
    - **Validates: Requirements 15.12**

  - [ ]* 11.10 Property test per l'esclusione dei campioni di bordo
    - **Property 11: Esclusione dei campioni di bordo proporzionale all'offset**
    - **Validates: Requirements 15.8, 15.9**

  - [ ] 11.11 Implementare `Verifier`, `AudioExtractor` e `SourceReader`
    - Associazione tracce-file (CUE o ordine alfabetico), pre-controlli (numero tracce, formato, durata vs offset), READ CD con C2, riletture con Cache_Defeat fino a N, ignorare i C2 se `c2_reliable=0`, blocchi non affidabili, CRC32 (`System.IO.Hashing`) e SHA-256, `TrackResult`, risultato IDENTICAL/DIFFERENT/ERROR, annullamento mai IDENTICAL
    - _Requirements: 13.5, 14.2-14.13, 14.16-14.18, 15.10, 15.11_

  - [ ]* 11.12 Scrivere i test unitari del Verifier
    - Con Fake_Transport e PCM sintetico: numero tracce diverso, formato non valido, durata incompatibile, errore READ CD con ERROR, C2 residui dopo N tentativi, annullamento
    - _Requirements: 14.3, 14.4, 14.11, 14.12, 14.16-14.18_

  - [ ] 11.13 Implementare la vista Verifica e le azioni cache/offset
    - Selezione Disco e Sorgente, avanzamento per traccia ≥ 1 volta/s, tabella esiti, "Disco identico alla sorgente", report con campioni esclusi e limitazioni Cache_Defeat, rilevamento automatico e inserimento manuale dell'offset, esecuzione del Cache_Detector all'aggiunta di un Drive, salvataggio della verifica
    - Icona `disccheck.verify.bitperfect`
    - _Requirements: 14.1, 14.9-14.13, 12.1, 12.4, 12.9, 15.2-15.7, 15.9_

  - [ ]* 11.14 Scrivere gli hardware test M5
    - Rilevamento cache su iHAS124 (confronto con 2.048 KB) e verifica bit-perfect di un disco noto
    - _Requirements: 12.10, 27.6, 27.11_

- [ ] 12. Checkpoint M5
  - Ensure all tests pass, ask the user if questions arise.
  - Verificare le condizioni M5 del Requirement 27.6.

- [ ] 13. M6: LiteOnScanner (CDB portati solo da QPxTool_Source)
  - [ ] 13.1 Risolvere la decisione D2 (licenza del porting)
    - Se D2 è "aperta", chiedere all'utente con `user_input` la scelta (GPL-2.0 per distribuzione oppure uso personale) e registrarla in `docs/decisions.md`
    - Se D2 resta "aperta", non avviare gli altri task di M6 e segnalare D2 come bloccante
    - _Requirements: 25.3, 25.6, 25.8_

  - [ ] 13.2 Estrarre da QPxTool_Source il flusso e l'elenco dei CDB
    - Leggere `plugins/liteon/qscan_cmd.cpp` e `qscan_plugin.cpp` del fork speed47; documentare in `docs/liteon-cdb.md` funzione, byte del CDB, direzione e lunghezza dati per `cmd_cd_errc_init`, lettura dei contatori e `cmd_cd_errc_end`
    - Confermare il flusso del Requirement 8.3 e la differenza tra Metodo_Nuovo e Metodo_Vecchio (`LITEON_FORCE_OLD`)
    - Nessun CDB può essere inserito se non è letto nel sorgente; se il sorgente non è accessibile, chiedere indicazioni all'utente
    - _Requirements: 8.1, 8.3, 8.4_

  - [ ] 13.3 Portare i comandi vendor in `DiscCheck.Scanners`
    - Un metodo per comando, con commento su file e funzione originali; intestazione per file con nome originale, copyright, "GPL-2.0" e nota di modifica; includere il testo GPL-2.0 se D2 richiede distribuzione
    - _Requirements: 8.1, 8.2, 25.7, 25.8_

  - [ ] 13.4 Implementare `LiteOnScanner`
    - `Probe` su vendor "LITE-ON" senza distinzione di maiuscole; flusso SET CD SPEED → init → lettura → fine scansione in `finally` (timeout 30 s); Metodo_Nuovo predefinito, Metodo_Vecchio da impostazione; un solo fallback automatico Nuovo → Vecchio; un campione al secondo con C1/C2/CU ≥ 0
    - _Requirements: 8.3-8.8, 8.11, 8.12, 23.8, 26.2_

  - [ ] 13.5 Integrare gli esiti e i messaggi di LiteOnScanner nell'App
    - Fallimento dell'init con proposta di fallback a StandardC2Scanner solo dopo conferma, messaggio sul bridge USB, avviso Test_Mode con "Reset drive", messaggio di firmware non compatibile dopo entrambi i metodi, avviso MediaTek su C1
    - _Requirements: 8.8-8.10, 11.7, 26.3, 26.4_

  - [ ]* 13.6 Scrivere i test di LiteOnScanner con fixture
    - Una fixture per ogni comando portato, un test per Metodo_Nuovo e uno per Metodo_Vecchio, `finally` su completamento/errore/annullamento, fallback, Probe con vendor in minuscolo
    - _Requirements: 8.6, 8.7, 8.13, 26.2, 23.8_

  - [ ]* 13.7 Scrivere gli hardware test M6
    - Scansione completa su iHAS124 e confronto con QPxTool/Opti Drive Control (C1 ±10%, C2 uguale); Test_Mode chiuso a fine scansione
    - _Requirements: 23.7, 27.7, 27.11_

- [ ] 14. Checkpoint M6
  - Ensure all tests pass, ask the user if questions arise.
  - Verificare le condizioni M6 del Requirement 27.7.

- [ ] 15. M7: Confronto, export, impostazioni e icone
  - [ ] 15.1 Implementare `SettingsValidator` e `SettingsStore`
    - `settings.json` con tutti i campi del design, validazione (soglie ≥ 0 con WARN < KO, velocità 1-52, N 1-10, timeout 1-300, anni 5-10, percorso non vuoto), default su file assente (creato) o non valido (invariato), salvataggio entro 1 s
    - _Requirements: 11.5, 11.9, 20.4, 20.12, 22.2-22.4, 22.9, 22.10_

  - [ ]* 15.2 Property test per il round-trip JSON delle impostazioni
    - **Property 16: Round-trip JSON delle impostazioni**
    - **Validates: Requirements 22.8**

  - [ ]* 15.3 Property test per la validazione delle Soglie
    - **Property 17: Validazione delle Soglie**
    - **Validates: Requirements 11.5, 11.9, 22.9**

  - [ ] 15.4 Implementare la vista Impostazioni e collegare le impostazioni
    - Modalità_Debug a runtime, cambio tema entro 1 s senza riavvio, cambio percorso Database con conferma e riapertura, errori senza modifica del file, uso di timeout/N/velocità/Soglie nei servizi
    - _Requirements: 16.2, 22.1, 22.5-22.7, 22.11_

  - [ ] 15.5 Implementare `ComparisonService`, `SpeedProposer` e vista Confronto
    - Da 2 a 8 scansioni con palette `Das.Color.Series.*`, tabella, avvisi (limite 8, una sola scansione, drive/velocità diversi), wizard velocità (minima supportata … 12x, nessuna velocità disponibile)
    - Icona `disccheck.compare.curves`
    - _Requirements: 19.1-19.7, 10.5, 35.4_

  - [ ]* 15.6 Scrivere i test unitari del Comparator e dello SpeedProposer
    - 8 scansioni esatte, 9 scansioni, una sola scansione, esclusione dei valori nulli, nessuna velocità supportata
    - _Requirements: 19.2, 19.4, 19.5, 19.6, 19.7_

  - [ ] 15.7 Implementare `CsvExporter` e `CsvImporter`
    - Intestazione `TimeSeconds,C1,C2,CU,SpeedX`, UTF-8, virgola, punto decimale, `\r\n`, nulli vuoti, indipendente dalla cultura; importer usato solo nei test
    - _Requirements: 21.1, 21.9_

  - [ ]* 15.8 Property test per il round-trip CSV
    - **Property 14: Round-trip CSV**
    - **Validates: Requirements 21.10**

  - [ ] 15.9 Implementare export PNG, report HTML e interfaccia di export
    - PNG ≥ 1.200×600 con zoom e serie correnti, HTML autosufficiente (Disco, Drive, Verdetto, riepilogo, grafico incorporato, verifica con Read_Offset, Combined_Offset, campioni esclusi), conferma di sovrascrittura, pulizia del file parziale, conferma con percorso entro 2 s, nessun export senza campioni, attribuzione D5 se richiesta
    - _Requirements: 21.2-21.8, 36.6_

  - [ ] 15.10 Creare le 15 icone del DAS_Icon_Set
    - SVG con `viewBox="0 0 24 24"`, tratto 1.5, `currentColor`, nomi file `<chiave>.svg`, risorse `Das.Icon.<chiave>`, stati distinti per forma
    - _Requirements: 33.1-33.7, 34.1, 34.3, 34.6, 34.7_

  - [ ] 15.11 Creare il DiscCheck_Icon_Pack e `disccheck.ico`
    - 5 icone `disccheck.*` in `src/DiscCheck.App/Assets/Icons/`, risorse `DiscCheck.Icon.<chiave>`, script riproducibile che genera `disccheck.ico` (7 dimensioni, badge) dai sorgenti SVG senza copiare il logo, impostazione dell'icona su eseguibile e finestra
    - _Requirements: 32.8-32.10, 34.2, 34.3, 35.7_

  - [ ]* 15.12 Applicare le icone a tutte le viste e creare la finestra About
    - Abbinamenti del Requirement 34.5 tramite `DasIcon`, `DasStatusBadge` per i Verdetti, About con lockup 64 px, versione app e `DasDesignInfo.Version`
    - _Requirements: 32.6, 34.5, 35.6_

  - [ ]* 15.13 Scrivere i test unitari dell'Exporter
    - File esistente, scrittura fallita con pulizia, nessun dato, struttura dell'HTML autosufficiente
    - _Requirements: 21.6-21.8_

  - [ ]* 15.14 Scrivere i test delle icone in DAS.Design.Tests
    - Validazione di ogni SVG, esattamente 20 chiavi senza duplicati, nessuna chiave `disccheck.*` in DAS.Design, `disccheck.ico` e `das.ico` con 7 dimensioni, nessuna copia del logo fuori da `src/DAS.Design/`
    - _Requirements: 37.3, 37.4, 37.9_

- [ ] 16. Checkpoint M7
  - Ensure all tests pass, ask the user if questions arise.
  - Verificare le condizioni M7 del Requirement 27.8.

- [ ] 17. M8: Archivio, degrado e C2_Reliability_Test
  - [ ] 17.1 Implementare `DegradationMonitor`
    - Serie di trend (C1 medio, C1 massimo, C2 totale) per data, `recheck_due` = data scansione + anni configurati (solo per il Disco scansionato, NULL senza scansioni), elenco scaduti e in scadenza (≤ 90 giorni), "in peggioramento"
    - _Requirements: 20.1-20.3, 20.5, 20.7, 20.10, 20.11_

  - [ ]* 17.2 Scrivere i test unitari del Degradation_Monitor
    - Una sola scansione, nessuna scansione, peggioramento OK→WARN→KO, confini a 90 giorni e a data odierna
    - _Requirements: 20.5, 20.7, 20.10, 20.11_

  - [ ] 17.3 Implementare la vista Archivio/Degrado e il promemoria all'avvio
    - Grafico di trend, elenco scaduti/in scadenza, evidenziazione "in peggioramento", suggerimenti di conservazione, longevità e obiettivo BLER < 50, promemoria una volta per avvio, errori di caricamento senza dati parziali
    - Icone `disccheck.archive.degradation` e `das.reminder.bell`
    - _Requirements: 20.1, 20.5, 20.6, 20.8, 20.9, 20.13_

  - [ ] 17.4 Implementare `C2ReliabilityAnalyzer`, test runner e interfaccia
    - Conferma e disco con difetto noto, due letture da 7.500 settori con Cache_Defeat, esiti Reliable/Unreliable(n)/Inconclusive, salvataggio di `drives.c2_reliable`, stati errore/annullato senza modifiche, rifiuto se il Drive non supporta C2
    - _Requirements: 13.1-13.8_

  - [ ]* 17.5 Scrivere i test unitari del C2_Reliability_Test
    - Reliable, Unreliable con conteggio, non conclusivo, errore/annullamento, Drive senza C2
    - _Requirements: 13.3, 13.4, 13.6, 13.7, 13.8_

  - [ ] 17.6 Risolvere la decisione D5 (licenza degli asset) prima della pubblicazione
    - Se D5 è "aperta", chiedere all'utente con `user_input` tra (a), (b) CC-BY 4.0 per gli asset grafici e (c) CC0 1.0; registrare opzione, data e motivazione in `docs/decisions.md`
    - Aggiornare `LICENSE-ASSETS.md` (titolare, anno, licenza, testo; attribuzione CC-BY nell'About se (b)) e rimuovere "licenza in definizione (D5)"; finché D5 è aperta, DAS.Design non va pubblicato
    - Registrare anche D3 e D4 se risolte, senza implementare masterizzazione
    - _Requirements: 25.4, 25.5, 36.3, 36.4, 36.6_

- [ ] 18. Checkpoint finale
  - Ensure all tests pass, ask the user if questions arise.
  - Verificare le condizioni M8 del Requirement 27.9 e `dotnet test --filter "Category!=Hardware"` con 0 falliti e 0 saltati.

## Notes

- I sotto-task marcati con `*` sono opzionali e possono essere saltati per un MVP più rapido.
- Ogni task cita i requisiti per tracciabilità; ogni property test cita il numero di proprietà del design.
- I checkpoint garantiscono la validazione incrementale per milestone (Requirement 27.10, 27.11).
- I task D1 (11.1), D2 (13.1) e D5 (17.6) richiedono una scelta dell'utente se la decisione è ancora "aperta".

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "1.3", "1.7", "3.1"] },
    { "id": 2, "tasks": ["1.4", "1.8", "3.2", "3.3", "5.1", "7.1", "7.4"] },
    { "id": 3, "tasks": ["1.5", "3.4", "5.2", "5.3", "5.4", "7.2", "7.3", "7.5", "7.6", "7.7", "7.8", "7.15", "9.1"] },
    { "id": 4, "tasks": ["1.6", "1.9", "3.7", "5.6", "7.9", "7.12", "7.16", "9.2"] },
    { "id": 5, "tasks": ["1.10", "3.5", "7.10", "7.11", "7.13", "7.14", "9.3", "9.4"] },
    { "id": 6, "tasks": ["3.6", "5.5"] },
    { "id": 7, "tasks": ["7.17"] },
    { "id": 8, "tasks": ["7.18", "9.5"] },
    { "id": 9, "tasks": ["9.6", "11.1"] },
    { "id": 10, "tasks": ["11.2", "11.4", "11.6", "11.8"] },
    { "id": 11, "tasks": ["11.3", "11.5", "11.7", "11.9", "11.10", "11.11"] },
    { "id": 12, "tasks": ["11.12", "11.13"] },
    { "id": 13, "tasks": ["11.14", "13.1"] },
    { "id": 14, "tasks": ["13.2"] },
    { "id": 15, "tasks": ["13.3"] },
    { "id": 16, "tasks": ["13.4"] },
    { "id": 17, "tasks": ["13.5", "13.6", "15.1", "15.7", "15.10", "15.11"] },
    { "id": 18, "tasks": ["13.7", "15.2", "15.3", "15.4", "15.8", "17.1"] },
    { "id": 19, "tasks": ["15.5", "15.14", "17.2"] },
    { "id": 20, "tasks": ["15.6", "15.9"] },
    { "id": 21, "tasks": ["15.12", "15.13"] },
    { "id": 22, "tasks": ["17.3", "17.6"] },
    { "id": 23, "tasks": ["17.4"] },
    { "id": 24, "tasks": ["17.5"] }
  ]
}
```
