# Requirements Document

## Introduction
DiscCheck (nome di lavoro, vedi D3) è un'applicazione desktop per Windows 10/11 x64 per il controllo qualità di CD audio masterizzati. Sostituisce programmi storici (Opti Drive Control 2020, Nero CD-DVD Speed, KProbe2, QPxTool) con uno strumento moderno che:

1. verifica che il contenuto del disco sia identico bit per bit ai file sorgente (FLAC/WAV 16 bit / 44,1 kHz);
2. misura la qualità fisica della scrittura (errori C1, C2, CU) per scegliere supporto e velocità di masterizzazione e per monitorare il degrado nel tempo;
3. archivia i risultati in un database locale SQLite.

Hardware di riferimento: drive Lite-On iHAS124-14 (SATA, MediaTek), box ICY BOX IB-525-U3 (bridge USB 3.0-SATA).

La masterizzazione, la conversione hi-res→16/44,1, le scansioni DVD/BD, il supporto Linux/macOS e i plugin per drive non Lite-On sono fuori scope v1 (vedi Requirement 24).

Le sezioni di riferimento (§) indicate nei requisiti rimandano alla specifica originale DiscCheck v0.2.

## Glossary
- **DiscCheck**: l'applicazione desktop oggetto di questo documento.
- **App**: il progetto `DiscCheck.App` (viste Avalonia, ViewModel, DI).
- **Core**: il progetto `DiscCheck.Core` (dominio, servizi, valutazione, interfacce).
- **Scsi_Layer**: il progetto `DiscCheck.Scsi` (trasporto e comandi MMC).
- **Scsi_Transport**: l'implementazione di `IScsiTransport`; in v1 solo `WinScsiPassThrough`.
- **WinScsiPassThrough**: implementazione Windows di `IScsiTransport` basata su `IOCTL_SCSI_PASS_THROUGH_DIRECT` (0x4D014).
- **Fake_Transport**: `FakeScsiTransport`, implementazione di `IScsiTransport` per i test, con risposte registrate in file `.bin` in `tests/Fixtures`.
- **CDB**: Command Descriptor Block SCSI.
- **Sense_Data**: dati di sense SCSI (Key, ASC, ASCQ).
- **MMC**: Multi-Media Commands (standard T10).
- **Drive**: unità ottica rilevata nel sistema.
- **Drive_Info**: informazioni di identificazione e capability di un Drive (vendor, modello, firmware, supporto C2, velocità).
- **Drive_Enumerator**: componente che elenca i Drive e li identifica con INQUIRY.
- **Disco**: il CD audio inserito nel Drive.
- **TOC**: Table of Contents del Disco (tracce, LBA, lead-out).
- **ATIP**: Absolute Time In Pre-groove, informazione di pre-groove dei CD-R.
- **Media_ID**: codice produttore del supporto ricavato dal tempo di inizio lead-in dell'ATIP.
- **ATIP_Table**: file JSON versionato `Core/Resources/atip-manufacturers.json` con la mappa Media_ID→produttore.
- **Quality_Scanner**: componente che implementa `IQualityScanner` (Name, Probe, ScanAsync).
- **StandardC2Scanner**: Quality_Scanner basato su READ CD con C2 error pointers, valido per qualunque Drive che dichiari supporto C2.
- **LiteOnScanner**: Quality_Scanner per misura C1/C2/CU su Drive Lite-On, basato su comandi vendor portati da QPxTool.
- **QPxTool_Source**: sorgente QPxTool (fork github.com/speed47/qpxtool, GPL-2.0), file `plugins/liteon/qscan_cmd.cpp` e `plugins/liteon/qscan_plugin.cpp`.
- **Metodo_Vecchio**: variante di scansione Lite-On selezionata dall'opzione `LITEON_FORCE_OLD` di QPxTool_Source.
- **Metodo_Nuovo**: variante di scansione Lite-On con la correzione presente nel fork speed47.
- **Scan_Sample**: campione di scansione `ScanSample(Lba, TimeSeconds, int? C1, C2, CU, double? SpeedX)`, con risoluzione di 1 campione al secondo di disco (75 settori).
- **Scan_Pipeline**: pipeline producer/consumer basata su `System.Threading.Channels.Channel` bounded.
- **Scan_Thread**: thread dedicato che esegue il ciclo di comandi SCSI e pubblica gli Scan_Sample.
- **Scan_Channel**: Channel bounded con capacità 1.024 e `FullMode=Wait`.
- **C1 / C2 / CU**: contatori di errore CIRC: C1 ≈ BLER (somma E11+E21+E31), C2 ≈ errori corretti di secondo livello (E12/E22), CU ≈ errori non correggibili (E32).
- **Verdetto**: esito della valutazione di una scansione: OK, WARN (attenzione) o KO.
- **Evaluator**: componente del Core che calcola il Verdetto dalle soglie.
- **Soglie**: parametri di valutazione configurabili in `settings.json`.
- **Settings_Store**: componente che legge e scrive `settings.json`.
- **Verifier**: il progetto `DiscCheck.Verify` (estrazione audio, offset, confronto).
- **Sorgente**: cartella o file CUE contenenti i file FLAC o WAV 16 bit / 44,1 kHz da confrontare con il Disco.
- **Read_Offset**: scostamento in campioni della lettura del Drive.
- **Combined_Offset**: somma di read offset e write offset, rilevata per allineamento con la Sorgente.
- **Cache_Defeat**: tecnica di rilettura che evita le risposte dalla cache del Drive, leggendo prima un'area lontana più grande della cache.
- **Cache_Detector**: componente che rileva la presenza e la dimensione della cache audio del Drive.
- **C2_Reliability_Test**: test opzionale di affidabilità dei puntatori C2 di un Drive.
- **Database**: file SQLite `%LOCALAPPDATA%\DiscCheck\disccheck.db`.
- **Repository**: classe di accesso ai dati basata su Dapper e Microsoft.Data.Sqlite.
- **Migrator**: componente che applica gli script SQL versionati e aggiorna `schema_version`.
- **Comparator**: componente che sovrappone e confronta due o più scansioni.
- **Degradation_Monitor**: componente che calcola i trend nel tempo e gestisce i promemoria di ricontrollo (`recheck_due`).
- **Exporter**: componente che produce export CSV, PNG e report HTML.
- **Log**: log Serilog su file e console.
- **Modalità_Debug**: impostazione che abilita il log Verbose dei CDB.
- **Test_Mode**: stato del Drive durante una scansione vendor, da chiudere con il comando di fine scansione.
- **Fixture**: file `.bin` con risposte SCSI registrate in `tests/Fixtures`.
- **DAS**: marchio e famiglia di applicazioni dello stesso tipo (strumenti desktop per dischi ottici e archivi audio) di cui DiscCheck è la prima applicazione; "DAS" è il nome del marchio, distinto dal nome dell'applicazione.
- **DAS_Design**: progetto/pacchetto `DAS.Design` (cartella `src/DAS.Design/`), libreria Avalonia 11 indipendente da DiscCheck che contiene Design_Token, stili, Icon_Set e DAS_Logo e che può essere referenziata da qualunque applicazione DAS (vedi Requirement 28).
- **Design_Token**: valore di design con nome semantico stabile (colore, tipografia, spaziatura, raggio), esposto come risorsa Avalonia con chiave nella forma `Das.<Categoria>.<Nome>` (esempio: `Das.Color.Primary`, `Das.Brush.Surface`).
- **Token_Key**: chiave di una risorsa Design_Token o di uno stile di DAS_Design usata nei file `.axaml` tramite `DynamicResource` o `StaticResource`.
- **Icon_Set**: insieme delle icone SVG di DAS_Design, suddiviso nel DAS_Icon_Set (icone generiche, riusabili da ogni applicazione DAS, chiavi `das.*`) e nel DiscCheck_Icon_Pack (icone specifiche di DiscCheck, chiavi `disccheck.*`).
- **DAS_Logo**: logo del marchio DAS (lockup orizzontale "disco + scritta DAS" e monogramma "solo disco") definito nel Requirement 31, identico in ogni applicazione DAS.
- **Theme_Entry_Point**: punto di ingresso unico di DAS_Design per registrare il tema in un'applicazione: il tipo `DasTheme` (Avalonia `Styles`) e il metodo di estensione `AppBuilder.UseDasTheme()` (vedi Requirement 28).
- **Accent_Override**: dizionario di risorse dell'applicazione che ridefinisce solo Design_Token accent, senza modificare DAS_Design.
- **Contrast_Ratio**: rapporto di contrasto WCAG 2.1 tra due colori, calcolato con la luminanza relativa sRGB.
- **DAS_Charts**: progetto/pacchetto `DAS.Design.Charts` (cartella `src/DAS.Design.Charts/`), libreria Avalonia 11 indipendente da DiscCheck che dipende solo da `DAS.Design` e da ScottPlot.Avalonia 5.x e che contiene Chart_Style, Chart_View, Chart_Cursor e Chart_Selection; `DAS.Design` resta privo di dipendenza da ScottPlot (Requirement 28 criterio 3, Requirement 30 criterio 9).
- **Chart_Style**: stile condiviso dei grafici, implementato dal tipo `DasChartStyle` e dal metodo `ChartThemeApplier.Apply(Plot, ThemeVariant)`, che applica ai grafici ScottPlot i Design_Token (colori, font Inter, griglia, assi, tick, legenda, sfondo) nelle varianti Light e Dark.
- **Chart_View**: controllo `DasChartView` di DAS_Charts che incorpora un `AvaPlot` di ScottPlot, applica il Chart_Style e fornisce Chart_Cursor, Chart_Selection, pulsante "Reset zoom", linee soglia ed evidenziazione degli errori; è l'unico modo in cui le viste di DiscCheck mostrano un grafico.
- **Chart_Cursor**: cursore verticale che segue il puntatore del mouse sul grafico, agganciato allo Scan_Sample più vicino nel tempo, con tooltip dei valori del campione.
- **Chart_Selection**: intervallo di tempo selezionato dall'utente sull'asse X di un Chart_View, per il quale sono mostrate le statistiche di minimo, massimo e media.

## Requirements
### Requirement 1: Struttura della soluzione e infrastruttura applicativa

**User Story:** Come sviluppatore, voglio una soluzione modulare con dependency injection e logging, così che i componenti siano sostituibili e testabili.

#### Acceptance Criteria

1. THE DiscCheck SHALL essere organizzato nella soluzione `DiscCheck.sln` con i progetti `DiscCheck.App`, `DiscCheck.Core`, `DiscCheck.Scsi`, `DiscCheck.Scanners`, `DiscCheck.Verify`, `DiscCheck.Data` in `src/`, il progetto `DAS.Design` in `src/DAS.Design/` (DAS_Design, Requirement 28), il progetto `DAS.Design.Charts` in `src/DAS.Design.Charts/` (DAS_Charts, Requirement 38) e `DiscCheck.Core.Tests`, `DiscCheck.Scsi.Tests`, `DiscCheck.Hardware.Tests`, `DAS.Design.Tests` in `tests/`.
2. THE DiscCheck SHALL essere implementato in C# 15 su .NET 11 (GA del 10 novembre 2026, STS, supportato fino a novembre 2028, SDK 11.0.x), con target framework `net11.0` per tutti i progetti (e `net11.0-windows` per i soli progetti Windows-specifici, solo se necessario), con Avalonia 11, CommunityToolkit.Mvvm, ScottPlot.Avalonia 5.x, Microsoft.Data.Sqlite, Dapper, Microsoft.Extensions.Hosting e Serilog.
3. WHEN l'App viene avviata, THE App SHALL registrare e risolvere servizi, Scsi_Transport, Quality_Scanner e Repository tramite Microsoft.Extensions.Hosting e dependency injection, senza istanziare direttamente implementazioni concrete di tali tipi nei ViewModel.
4. WHEN l'App viene avviata, THE Log SHALL scrivere su file e su console tramite Serilog, con livello minimo Information e livello Verbose solo mentre la Modalità_Debug è attiva.
5. IF l'inizializzazione del Log su file fallisce (ad esempio cartella non scrivibile), THEN THE App SHALL proseguire l'avvio scrivendo solo su console e mostrare un avviso che indica che il log su file non è disponibile.
6. THE Scsi_Layer SHALL esporre tutte le operazioni hardware esclusivamente tramite l'interfaccia `IScsiTransport`, senza che App, Core, Scanners e Verifier invochino direttamente `CreateFile` o `DeviceIoControl`.
7. THE ViewModel SHALL limitarsi all'orchestrazione dei servizi e delegare al Core ogni logica di dominio (valutazione dei Verdetti, calcolo di `toc_hash`, calcolo dei trend), senza riferimenti a `DiscCheck.Scsi` o `DiscCheck.Data` diversi dalle interfacce definite nel Core.
8. WHEN l'App viene avviata, THE App SHALL mostrare la finestra principale con tema Fluent chiaro o scuro, applicato tramite il Theme_Entry_Point di DAS_Design (Requirement 28) e seguendo il tema di sistema di Windows, entro 5 secondi dall'avvio su un PC che soddisfa i requisiti di Windows 10/11 x64.
9. IF l'avvio dell'App fallisce per un'eccezione non gestita nella costruzione dell'host o nella risoluzione dei servizi, THEN THE App SHALL scrivere l'eccezione nel Log, mostrare un messaggio di errore che indica l'impossibilità di avviare l'applicazione e terminare con codice di uscita diverso da 0.
10. THE DiscCheck SHALL includere nella root del repository un file `CLAUDE.md` con stack, struttura, regole di progetto e i comandi `dotnet build`, `dotnet test --filter "Category!=Hardware"`, `dotnet run --project src/DiscCheck.App`.
11. THE DiscCheck SHALL includere nella radice del repository un file `global.json` che fissa l'SDK 11.0.x con `rollForward` impostato a `latestFeature` e un file `Directory.Build.props` con `TargetFramework` `net11.0`, `LangVersion` non specificata (valore predefinito C# 15) e `Nullable` abilitato.
12. THE DiscCheck SHALL non usare funzionalità di linguaggio o di runtime in anteprima o sperimentali (ad esempio funzionalità C# disponibili solo in preview), in modo che la build usi solo l'SDK stabile.
13. THE DiscCheck SHALL eseguire la build nel workflow di CI e nel workflow di release con la stessa versione major dell'SDK fissata in `global.json`.

### Requirement 2: Trasporto SCSI su Windows

**User Story:** Come utente, voglio che DiscCheck comunichi direttamente con il Drive tramite SCSI pass-through, così che possa eseguire comandi MMC e vendor.

#### Acceptance Criteria

1. THE Scsi_Transport SHALL implementare `ScsiResult Execute(ReadOnlySpan<byte> cdb, Span<byte> data, DataDirection direction, TimeSpan timeout)`, dove `cdb` ha lunghezza di 6, 10, 12 o 16 byte e `DataDirection` assume i valori None, In (dal Drive all'host) e Out (dall'host al Drive).
2. THE ScsiResult SHALL contenere `ScsiStatus` (valore a 1 byte), `SenseData? Sense` (Key, ASC, ASCQ) valorizzato solo se il Drive restituisce sense e `BytesTransferred` (intero compreso tra 0 e la lunghezza di `data`).
3. WHEN WinScsiPassThrough apre un Drive, THE WinScsiPassThrough SHALL chiamare `CreateFile` su `\\.\<lettera>:` con `GENERIC_READ|GENERIC_WRITE`, `FILE_SHARE_READ|FILE_SHARE_WRITE` e `OPEN_EXISTING`.
4. WHEN WinScsiPassThrough esegue un comando, THE WinScsiPassThrough SHALL usare `DeviceIoControl` con `IOCTL_SCSI_PASS_THROUGH_DIRECT` (0x4D014), una struttura `SCSI_PASS_THROUGH_DIRECT` e un sense buffer di 32 byte.
5. WHEN WinScsiPassThrough prepara il buffer dati, THE WinScsiPassThrough SHALL usare un buffer pinnato e allineato secondo l'`AlignmentMask` restituito da `IOCTL_SCSI_GET_CAPABILITIES`.
6. THE WinScsiPassThrough SHALL applicare un timeout predefinito di 30 secondi, configurabile tra 1 e 600 secondi, e di 60 secondi per i comandi di avvio scansione.
7. THE App SHALL includere un `app.manifest` con `requireAdministrator`.
8. IF un comando SCSI termina con status diverso da GOOD (0x00), THEN THE Scsi_Transport SHALL restituire in `ScsiResult` lo status e i Sense_Data senza sollevare eccezioni.
9. IF l'apertura del Drive fallisce per privilegi insufficienti, THEN THE App SHALL mostrare un messaggio che indica la necessità di eseguire l'App come amministratore e SHALL non eseguire alcun comando sul Drive.
10. WHILE la Modalità_Debug è attiva, THE Scsi_Transport SHALL scrivere nel Log a livello Verbose, per ogni comando, il CDB, lo status, il sense e i primi 64 byte della risposta (o tutti i byte, se la risposta è più corta di 64 byte).
11. THE Scsi_Transport SHALL riutilizzare un buffer nativo allineato per ciclo di lettura, allocato con `NativeMemory.AlignedAlloc`, senza allocazioni gestite per singolo comando.
12. IF l'apertura del Drive fallisce per un motivo diverso dai privilegi insufficienti (lettera inesistente, unità non pronta o già in uso in modo esclusivo), THEN THE WinScsiPassThrough SHALL restituire un errore che indica la lettera del Drive e il codice di errore di Windows, senza sollevare eccezioni non gestite, e THE App SHALL mostrare un messaggio che indica la causa.
13. IF `DeviceIoControl` restituisce errore prima del completamento del comando (ad esempio per rimozione del Drive o disconnessione del bridge USB), THEN THE WinScsiPassThrough SHALL restituire un errore di trasporto che indica il codice di errore di Windows, distinto da uno status SCSI diverso da GOOD, senza sollevare eccezioni.
14. IF un comando non termina entro il timeout specificato, THEN THE WinScsiPassThrough SHALL restituire un errore di timeout che indica il CDB e il timeout applicato, senza sollevare eccezioni, e SHALL non eseguire alcun nuovo tentativo automatico del comando.

### Requirement 3: Comandi MMC

**User Story:** Come sviluppatore, voglio una libreria dei comandi MMC con parsing verificato, così che i servizi di livello superiore non gestiscano CDB grezzi.

#### Acceptance Criteria

1. THE Scsi_Layer SHALL implementare i comandi INQUIRY (0x12), MODE SENSE(10) (0x5A), GET CONFIGURATION (0x46), READ TOC/PMA/ATIP (0x43), READ CD (0xBE), SET CD SPEED (0xBB), TEST UNIT READY (0x00) e START STOP UNIT (0x1B), ciascuno esposto come metodo tipizzato che costruisce il CDB e interpreta la risposta, senza che i chiamanti di livello superiore debbano manipolare byte grezzi.
2. WHEN viene eseguito INQUIRY, THE Scsi_Layer SHALL restituire vendor (8 caratteri ASCII), prodotto (16 caratteri ASCII) e revisione firmware (4 caratteri ASCII), privati degli spazi finali.
3. WHEN viene eseguito MODE SENSE(10) sulla pagina 0x2A, THE Scsi_Layer SHALL restituire il flag "C2 Pointers supported" (vero o falso) e le velocità supportate in kB/s e in multipli di 1x (1x = 176 kB/s).
4. WHEN viene eseguito SET CD SPEED, THE Scsi_Layer SHALL codificare la velocità richiesta in kB/s come intero senza segno a 16 bit big-endian, con 1x = 176 kB/s (esempio: 8x = 1.408 kB/s) e il valore 0xFFFF per la velocità massima.
5. IF la velocità richiesta per SET CD SPEED è 0 oppure supera 0xFFFE kB/s senza essere la velocità massima, THEN THE Scsi_Layer SHALL rifiutare la richiesta con un errore di argomento non valido che indica il valore ricevuto, senza inviare alcun comando al Drive.
6. WHEN il parser riceve una risposta Sense_Data, THE Scsi_Layer SHALL estrarre Key (4 bit), ASC (8 bit) e ASCQ (8 bit) dai Sense_Data in formato fixed.
7. IF una risposta è più corta della lunghezza minima prevista per il comando (INQUIRY: 36 byte; MODE SENSE(10) pagina 0x2A: 24 byte; Sense_Data: 14 byte; per gli altri comandi la lunghezza minima è documentata nel codice del comando), THEN THE Scsi_Layer SHALL restituire un errore di parsing che indica il comando, la lunghezza minima attesa e la lunghezza ricevuta, senza restituire valori parziali.
8. IF un comando MMC termina con status diverso da GOOD, THEN THE Scsi_Layer SHALL restituire un errore di comando che riporta nome del comando, status e Sense_Data (Key, ASC, ASCQ), senza tentare il parsing dei dati di risposta e senza sollevare eccezioni non gestite.
9. THE Scsi_Layer SHALL fornire per ogni comando elencato nel criterio 1 almeno un test con Fake_Transport e risposta registrata in una Fixture, e almeno un test con risposta troncata che verifica l'errore di parsing del criterio 7.

### Requirement 4: Rilevamento e identificazione dei Drive

**User Story:** Come utente, voglio vedere i drive ottici disponibili con le loro capability, così che possa scegliere quello da usare.

#### Acceptance Criteria

1. WHEN l'utente apre la vista Drive, THE Drive_Enumerator SHALL elencare tutte le unità restituite da `DriveInfo.GetDrives()` con `DriveType` uguale a CDRom, indipendentemente dalla presenza di un Disco inserito, e THE App SHALL mostrare per ciascuna la lettera di unità.
2. WHEN il Drive_Enumerator elenca un'unità, THE Drive_Enumerator SHALL eseguire INQUIRY con il timeout predefinito di 30 secondi (Requirement 2) e ottenere vendor (8 caratteri), modello (16 caratteri) e revisione firmware (4 caratteri), eliminando gli spazi finali.
3. WHEN un'unità è identificata, THE App SHALL mostrare nella vista Drive vendor, modello, firmware, supporto ai puntatori C2 ("supportati" o "non supportati" secondo MODE SENSE pagina 0x2A), Read_Offset in campioni, esito del rilevamento cache ("rilevata" con dimensione in kB, "non rilevata" oppure "non eseguito") e affidabilità C2 ("affidabile", "non affidabile" oppure "non testata"). Per i valori non ancora determinati l'App SHALL mostrare "non determinato" o "non eseguito" in luogo di un valore numerico.
4. WHEN il Drive di riferimento Lite-On iHAS124-14 è collegato tramite ICY BOX IB-525-U3 ed è identificato, THE App SHALL mostrare un modello che inizia con "iHAS124" e vendor "LITE-ON", la revisione firmware restituita da INQUIRY e lo stato del supporto C2.
5. WHEN un Drive identificato non ha nella tabella `drives` una riga con la stessa terna (vendor, model, firmware), THE Repository SHALL inserire una sola nuova riga con tale terna. WHEN la riga esiste già, THE Repository SHALL non creare duplicati.
6. IF l'INQUIRY di un'unità fallisce per status SCSI diverso da GOOD, per timeout o per errore di apertura, THEN THE Drive_Enumerator SHALL mostrare l'unità come "non identificabile" con l'indicazione del motivo (status e Sense_Data, timeout oppure errore di apertura), non salvarla nella tabella `drives` e continuare l'enumerazione delle altre unità.
7. IF `DriveInfo.GetDrives()` non restituisce alcuna unità di tipo CDRom, THEN THE App SHALL mostrare nella vista Drive un messaggio che indica l'assenza di drive ottici e un elenco di Drive vuoto.
8. WHEN l'utente seleziona un Drive nella vista Drive, THE App SHALL rendere disponibile il comando "Reset drive".
9. WHEN l'utente esegue "Reset drive", THE Scsi_Layer SHALL inviare il comando di fine scansione, poi TEST UNIT READY, e THE App SHALL mostrare l'esito "Drive ripristinato" se TEST UNIT READY termina con status GOOD oppure con Sense_Data di "Disco assente" (Drive pronto a ricevere comandi senza Disco).
10. IF il comando di fine scansione o TEST UNIT READY di "Reset drive" termina con errore per status diverso da GOOD (salvo il caso di Disco assente) o per timeout, THEN THE App SHALL mostrare un messaggio di errore con Sense_Data oppure l'indicazione di timeout, registrare nel Log CDB e Sense_Data e lasciare il Drive selezionato e disponibile per un nuovo tentativo.

### Requirement 5: Lettura di TOC, ATIP e Media ID

**User Story:** Come utente, voglio leggere TOC e ATIP del disco inserito, così che possa conoscere tracce, durata e produttore del supporto.

#### Acceptance Criteria

1. WHEN l'utente apre la vista Disco con un Disco inserito, THE Scsi_Layer SHALL leggere la TOC con READ TOC formato 0, ottenendo numero di tracce (da 1 a 99), LBA di inizio di ogni traccia e LBA del lead-out.
2. WHEN la TOC è letta, THE App SHALL mostrare per ogni traccia numero, LBA di inizio e durata nel formato mm:ss (secondi arrotondati per difetto, 75 settori = 1 secondo), oltre al totale dei settori del Disco, entro 2 secondi dalla ricezione della risposta del Drive.
3. WHEN il Disco è un CD-R, THE Scsi_Layer SHALL leggere l'ATIP con READ TOC formato 4 e ricavare il tempo di inizio lead-in nel formato mm:ss:ff, con minuti 0-99, secondi 0-59 e frame 0-74.
4. WHEN il tempo di inizio lead-in è noto, THE Core SHALL cercare il produttore nella ATIP_Table con corrispondenza esatta sul valore mm:ss:ff e l'App SHALL mostrare Media_ID (nel formato mm:ss:ff) e produttore.
5. IF il Media_ID non è presente nella ATIP_Table, THEN THE App SHALL mostrare il Media_ID nel formato mm:ss:ff con la dicitura "produttore sconosciuto".
6. THE ATIP_Table SHALL essere un file JSON che contiene un campo di versione intero maggiore o uguale a 1 e una voce per ogni Media_ID univoco.
7. THE ATIP_Table SHALL includere solo voci con una fonte pubblica di verifica indicata, e i codici 97:24:01 e 97:34:23 SHALL essere esclusi finché non sono confermati su tale fonte.
8. WHEN la TOC è letta, THE Core SHALL calcolare un `toc_hash` deterministico dalla TOC, in modo che due letture della stessa TOC producano lo stesso `toc_hash` e due TOC con almeno una traccia o un LBA diverso producano `toc_hash` diversi.
9. WHEN l'utente associa il Disco a un supporto, THE Repository SHALL salvare l'associazione tra `discs.media_id` e la riga `media` in un'unica transazione.
10. IF il Disco non è presente o non è un CD audio, THEN THE App SHALL mostrare un messaggio che indica il motivo (nessun disco, disco non CD audio o Drive non pronto) e non mostrare dati di TOC o ATIP del Disco precedente.
11. FOR ALL TOC valide, il parsing seguito da serializzazione e nuovo parsing SHALL produrre una TOC equivalente (round-trip) su dati di test.
12. IF la lettura della TOC fallisce o la risposta è più corta della lunghezza minima prevista, THEN THE App SHALL mostrare un messaggio di errore che indica Sense_Data (Key, ASC, ASCQ) o motivo del parsing e non salvare alcun dato del Disco nel Database.
13. IF il Disco non è un CD-R, oppure la lettura dell'ATIP fallisce, THEN THE App SHALL mostrare la TOC letta, indicare che Media_ID e produttore non sono disponibili con il motivo (supporto non CD-R o errore di lettura ATIP) e consentire comunque il salvataggio del Disco senza `media_id`.
14. IF la ATIP_Table è assente, non è un JSON valido o ha un campo di versione mancante, THEN THE Core SHALL registrare l'errore nel Log e THE App SHALL mostrare il Media_ID con la dicitura "produttore sconosciuto".

### Requirement 6: Architettura a plugin degli scanner

**User Story:** Come utente, voglio che DiscCheck proponga lo scanner più adatto al mio drive, così che possa ottenere la misura migliore disponibile.

#### Acceptance Criteria

1. THE Quality_Scanner SHALL implementare `IQualityScanner` con `Name` (stringa non vuota, univoca tra gli scanner registrati), `ScannerCapabilities Probe(DriveInfoEx)` e `IAsyncEnumerable<ScanSample> ScanAsync(ScanOptions, IProgress<ScanProgress>, CancellationToken)`, dove `ScannerCapabilities` indica se il Drive è supportato, quali tra C1, C2 e CU lo scanner misura e, se il Drive non è supportato, il motivo.
2. WHEN l'utente seleziona un Drive, THE Core SHALL invocare `Probe()` sugli scanner registrati in ordine di priorità fisso (LiteOnScanner, poi StandardC2Scanner), interrompere l'invocazione al primo scanner che dichiara di supportare il Drive e proporre quello scanner mostrandone il `Name`, entro 5 secondi dalla selezione.
3. WHERE l'utente sceglie di forzare lo scanner standard e StandardC2Scanner dichiara di supportare il Drive, THE App SHALL usare StandardC2Scanner indipendentemente dal risultato di `Probe()` degli altri scanner.
4. IF l'utente sceglie di forzare lo scanner standard e StandardC2Scanner non dichiara di supportare il Drive, THEN THE App SHALL non avviare la scansione e mostrare un messaggio che indica il motivo restituito da `Probe()`.
5. IF `Probe()` di uno scanner solleva un'eccezione o non termina entro 5 secondi, THEN THE Core SHALL considerare quello scanner non supportante, registrare l'errore nel Log e proseguire con lo scanner successivo.
6. IF nessuno scanner supporta il Drive, THEN THE App SHALL mostrare un messaggio che indica che il Drive non supporta la scansione di qualità e disabilitare il comando di avvio scansione per quel Drive.
7. THE Core e l'App SHALL consentire l'aggiunta di un nuovo scanner per Drive non Lite-On tramite la sola registrazione dell'implementazione di `IQualityScanner` nella dependency injection, senza modifiche al codice di Core e App, verificato da un test con uno scanner fittizio registrato (predisposizione; nessun plugin non Lite-On implementato in v1).

### Requirement 7: StandardC2Scanner

**User Story:** Come utente, voglio scansionare i C2 su qualunque drive che li supporti, così che possa avere un indicatore universale di qualità.

#### Acceptance Criteria

1. WHEN viene eseguito `Probe()`, THE StandardC2Scanner SHALL dichiarare il supporto se e solo se la risposta di MODE SENSE pagina 0x2A indica "C2 Pointers supported".
2. IF MODE SENSE pagina 0x2A termina con status diverso da GOOD oppure restituisce una risposta più corta della lunghezza minima prevista, THEN THE StandardC2Scanner SHALL dichiarare il Drive non supportato e registrare nel Log Sense_Data o motivo dell'errore di parsing.
3. WHEN la scansione è avviata, THE StandardC2Scanner SHALL leggere il Disco in modo sequenziale e senza salti, dall'LBA 0 al lead-out indicato dalla TOC, con READ CD, tipo di settore CD-DA (0x04 nel byte 1) e blocchi di 26 o 27 settori, con dimensione costante per l'intera scansione e ultimo blocco limitato ai settori residui.
4. THE StandardC2Scanner SHALL richiedere user data più C2 error pointers con il valore 0x12 nel byte 9 (2352+294 = 2646 byte per settore) oppure con il valore 0x14 con block error byte (2352+296 = 2648 byte per settore), usando lo stesso valore per l'intera scansione.
5. WHEN un settore è letto, THE StandardC2Scanner SHALL calcolare i C2 del settore come numero di bit a 1 nei 294 byte di puntatori C2, escludendo dal conteggio il block error byte e il byte di riempimento del formato 0x14.
6. WHEN sono stati letti 75 settori consecutivi, THE StandardC2Scanner SHALL pubblicare uno Scan_Sample con `TimeSeconds` pari all'indice del secondo di disco, C2 pari alla somma dei C2 dei 75 settori, C1 nullo e CU pari a 0.
7. WHEN la lettura raggiunge il lead-out con un ultimo gruppo da 1 a 74 settori, THE StandardC2Scanner SHALL pubblicare un ultimo Scan_Sample con la somma dei C2 dei settori residui.
8. WHEN la scansione è in corso, THE StandardC2Scanner SHALL calcolare `SpeedX` di ogni Scan_Sample come numero di settori letti nel secondo, diviso per 75 e per il tempo reale trascorso in secondi per leggerli, e riportarlo nello Scan_Sample con risoluzione di almeno 0,1x.
9. THE App SHALL mostrare nella vista Scansione i limiti noti: misura solo C2 e non C1, valori dipendenti dal firmware, indicatore universale e non misura di precisione.
10. IF un comando READ CD termina con status diverso da GOOD, THEN THE StandardC2Scanner SHALL registrare nel Log LBA e Sense_Data, interrompere la scansione senza ritentare il comando, mostrare all'utente un messaggio che indica LBA e Sense_Data (Key, ASC, ASCQ) e conservare gli Scan_Sample già pubblicati.
11. FOR ALL pattern di 294 byte, il conteggio dei bit a 1 SHALL essere uguale alla somma dei conteggi delle due metà da 147 byte (invariante di additività).

### Requirement 8: LiteOnScanner (C1/C2/CU)

**User Story:** Come utente con un Drive Lite-On, voglio misurare C1, C2 e CU, così che possa valutare la qualità fisica della scrittura.

#### Acceptance Criteria

1. THE LiteOnScanner SHALL ricavare ogni CDB vendor esclusivamente da QPxTool_Source (`plugins/liteon/qscan_cmd.cpp` e `plugins/liteon/qscan_plugin.cpp`), senza CDB dedotti o inventati.
2. THE LiteOnScanner SHALL riportare, nel commento di ogni comando portato, il file e la funzione originali di QPxTool_Source.
3. WHEN la scansione è avviata, THE LiteOnScanner SHALL eseguire nell'ordine: `Probe()`, SET CD SPEED alla velocità selezionata, `cmd_cd_errc_init`, ciclo di lettura blocchi con lettura dei contatori C1, C2 e CU, `cmd_cd_errc_end` (flusso da confermare sul sorgente QPxTool_Source prima dell'implementazione).
4. THE LiteOnScanner SHALL implementare sia il Metodo_Nuovo sia il Metodo_Vecchio (`LITEON_FORCE_OLD`), con il Metodo_Nuovo come valore predefinito.
5. WHERE l'utente seleziona il Metodo_Vecchio nelle impostazioni, THE LiteOnScanner SHALL usare il Metodo_Vecchio per tutta la durata della scansione.
6. WHEN la scansione termina per completamento, cancellazione o errore, THE LiteOnScanner SHALL eseguire `cmd_cd_errc_end` in un blocco `finally`, con timeout di 30 secondi, e riportare il Drive fuori dal Test_Mode.
7. WHEN `Probe()` riceve un INQUIRY con vendor "LITE-ON" (confronto senza distinzione tra maiuscole e minuscole), THE LiteOnScanner SHALL dichiarare il supporto alla scansione C1/C2/CU; altrimenti THE LiteOnScanner SHALL dichiarare la scansione non supportata.
8. IF il Drive rifiuta `cmd_cd_errc_init` con status diverso da GOOD oppure non risponde entro 60 secondi, THEN THE LiteOnScanner SHALL registrare nel Log CDB e Sense_Data, non avviare la scansione e proporre all'utente il fallback a StandardC2Scanner, che parte solo dopo conferma dell'utente.
9. IF `cmd_cd_errc_init` fallisce e il Drive è collegato tramite bus USB, THEN THE App SHALL mostrare un messaggio che indica che il bridge USB potrebbe filtrare i comandi vendor e suggerisce il collegamento SATA interno.
10. IF il Drive resta in Test_Mode dopo un arresto anomalo oppure `cmd_cd_errc_end` fallisce, THEN THE App SHALL mostrare un messaggio che segnala il Test_Mode attivo e consentire all'utente di eseguire il comando "Reset drive".
11. IF un comando del ciclo di lettura termina con status diverso da GOOD o con timeout di 30 secondi, THEN THE LiteOnScanner SHALL registrare nel Log CDB e Sense_Data, interrompere la scansione, conservare gli Scan_Sample già pubblicati e riportare all'utente un errore che indica LBA e Sense_Data.
12. WHEN la scansione termina con successo, THE LiteOnScanner SHALL aver prodotto uno Scan_Sample ogni secondo di disco (75 settori), contenente C1, C2 e CU come interi maggiori o uguali a 0 e `SpeedX` come velocità effettiva misurata.
13. THE LiteOnScanner SHALL essere testato con Fake_Transport e almeno una Fixture con risposta registrata per ogni comando portato, includendo un test per il Metodo_Nuovo e uno per il Metodo_Vecchio.

### Requirement 9: Pipeline di scansione asincrona producer/consumer

**User Story:** Come utente, voglio che la scansione sia regolare e cancellabile mentre l'interfaccia resta reattiva, così che i grafici non siano falsati da pause.

#### Acceptance Criteria

1. WHEN la scansione è avviata, THE Scan_Pipeline SHALL eseguire il ciclo di comandi SCSI su un unico Scan_Thread dedicato, distinto dal thread dell'interfaccia utente.
2. WHEN lo Scan_Thread completa la misura di 1 secondo di disco (75 settori), THE Scan_Thread SHALL pubblicare il relativo Scan_Sample nello Scan_Channel, che ha capacità 1.024 Scan_Sample e `FullMode=Wait`.
3. WHILE lo Scan_Channel contiene meno di 1.024 Scan_Sample, THE Scan_Thread SHALL eseguire i comandi SCSI successivi senza attendere il consumo degli Scan_Sample né il rendering dell'interfaccia.
4. WHILE lo Scan_Channel contiene 1.024 Scan_Sample, THE Scan_Thread SHALL attendere che si liberi almeno 1 posizione prima di pubblicare, senza scartare né sovrascrivere Scan_Sample.
5. THE Scan_Pipeline SHALL avere 2 consumer indipendenti dallo Scan_Thread, uno per l'App (grafico e progresso) e uno per l'accumulo in memoria degli Scan_Sample destinati al Repository, e ciascun consumer SHALL ricevere tutti gli Scan_Sample pubblicati.
6. WHEN uno Scan_Sample è pubblicato, THE App SHALL aggiornare il progresso tramite `IProgress<ScanProgress>` entro 1 secondo dalla pubblicazione, con LBA corrente, percentuale completata (0-100) e `SpeedX`.
7. WHEN l'utente preme Annulla, THE Scan_Pipeline SHALL propagare il `CancellationToken` allo Scan_Thread, interrompere la scansione prima del comando SCSI successivo e inviare il comando di fine scansione per chiudere il Test_Mode del Drive.
8. WHEN l'utente preme Annulla, THE App SHALL tornare allo stato inattivo entro 5 secondi dalla pressione, con Test_Mode chiuso e senza salvare la scansione annullata nel Database.
9. IF il consumer del grafico è più lento dello Scan_Thread, THEN THE Scan_Pipeline SHALL consegnare a ogni consumer tutti gli Scan_Sample nello stesso ordine di pubblicazione, senza perdite né duplicati.
10. FOR ALL sequenze di Scan_Sample pubblicate, l'ordine di consumo SHALL coincidere con l'ordine di pubblicazione e il numero di campioni consumati SHALL essere uguale a quello pubblicato (invariante).
11. IF lo Scan_Thread termina per errore, THEN THE Scan_Pipeline SHALL chiudere lo Scan_Channel segnalando l'errore ai consumer dopo la consegna degli Scan_Sample già pubblicati, chiudere il Test_Mode del Drive e l'App SHALL mostrare un messaggio che indica la causa dell'errore e tornare allo stato inattivo entro 5 secondi.
12. IF il comando di fine scansione fallisce durante l'annullamento o dopo un errore, THEN THE App SHALL registrare nel Log CDB e Sense_Data, mostrare un messaggio che indica che il Drive può essere ancora in Test_Mode e rendere disponibile il comando "Reset drive".

### Requirement 10: Velocità di scansione

**User Story:** Come utente, voglio scegliere la velocità di scansione e capire quando due scansioni sono confrontabili.

#### Acceptance Criteria

1. THE App SHALL offrire nel selettore di velocità esattamente 5 voci (4x, 8x, 16x, 24x e massima), con 8x CLV preselezionata a ogni avvio dell'App. La velocità "massima" corrisponde alla velocità massima di lettura dichiarata in Drive_Info.
2. WHEN la scansione è avviata, THE Scsi_Layer SHALL inviare il comando SET CD SPEED con la velocità selezionata, prima del primo comando di lettura della scansione.
3. WHILE la scansione è in corso, THE App SHALL aggiornare la curva grigia della velocità effettiva con il valore `SpeedX` di ogni Scan_Sample, entro 1 secondo dalla pubblicazione del campione.
4. WHEN uno Scan_Sample ha `SpeedX` nullo, THE App SHALL interrompere la curva grigia in corrispondenza di quel campione, senza interpolare né mostrare il valore 0.
5. WHILE la vista Scansione o la vista Confronto è visualizzata, THE App SHALL mostrare l'avviso "Confronta solo scansioni fatte con lo stesso drive alla stessa velocità".
6. WHEN l'utente seleziona una velocità non supportata dal Drive, THE App SHALL indicare la velocità supportata più vicina (a parità di distanza, la minore) e chiedere conferma con le azioni Conferma e Annulla. Se l'utente conferma, THE App SHALL usare la velocità indicata. Se l'utente annulla, THE App SHALL mantenere la selezione precedente e non avviare la scansione.
7. IF il comando SET CD SPEED termina con Sense_Data di errore o non risponde entro 10 secondi, THEN THE Scsi_Layer SHALL non avviare la lettura e chiudere il Test_Mode del Drive se aperto. THE App SHALL tornare allo stato inattivo entro 5 secondi, mostrare un messaggio di errore che indica il rifiuto della velocità e non salvare alcuna scansione.
8. WHEN la velocità effettiva del Drive cambia durante la scansione, THE Scan_Pipeline SHALL pubblicare esattamente 1 Scan_Sample ogni 75 settori di disco, senza campioni persi, duplicati o fuori ordine.

### Requirement 11: Valutazione automatica

**User Story:** Come utente, voglio un verdetto OK/Attenzione/KO per ogni scansione, così che possa decidere rapidamente se il disco è affidabile.

#### Acceptance Criteria

1. WHEN una scansione termina con almeno 1 Scan_Sample, THE Evaluator SHALL calcolare il Verdetto entro 1 secondo, per scansioni fino a 6.000 Scan_Sample, usando queste statistiche: C2 totale (somma di C2 su tutti i campioni), CU totale (somma di CU su tutti i campioni), C1 medio (media aritmetica di C1 in errori/s sui soli campioni con C1 non nullo) e C1 massimo (valore massimo di C1 in errori/s tra i campioni con C1 non nullo).
2. IF C2 totale = 0 e CU totale = 0 e C1 medio < 1/s e C1 massimo < 50/s, THEN THE Evaluator SHALL assegnare il Verdetto OK.
3. IF C2 totale = 0 e CU totale = 0 e (C1 medio ≥ 1/s oppure C1 massimo ≥ 50/s) e C1 massimo < 220/s, THEN THE Evaluator SHALL assegnare il Verdetto WARN.
4. IF C2 totale > 0 oppure CU totale > 0 oppure C1 massimo ≥ 220/s, THEN THE Evaluator SHALL assegnare il Verdetto KO.
5. THE Settings_Store SHALL rendere configurabili in `settings.json` tutte le Soglie usate nei criteri 2, 3 e 4, con valori predefiniti C1 medio = 1/s, C1 massimo per WARN = 50/s e C1 massimo per KO = 220/s, e SHALL accettare solo valori numerici ≥ 0 con soglia C1 massimo per WARN < soglia C1 massimo per KO.
6. WHEN una scansione è eseguita con StandardC2Scanner, THE Evaluator SHALL assegnare il Verdetto OK se C2 totale = 0 e CU totale = 0, il Verdetto KO altrimenti, senza mai assegnare WARN e senza applicare le Soglie C1.
7. WHEN il Verdetto di una scansione eseguita con LiteOnScanner è mostrato, THE App SHALL mostrare accanto al Verdetto la nota che il C1 dei drive PC è un'approssimazione del BLER e va usato per confronti relativi sullo stesso drive alla stessa velocità.
8. FOR ALL coppie di insiemi di campioni A e B, dove B è ottenuto da A aggiungendo errori C2 o CU, il Verdetto di B SHALL essere uguale o peggiore di quello di A, con ordine OK < WARN < KO.
9. IF il file `settings.json` contiene almeno una Soglia non valida (non numerica, negativa o in violazione dell'ordine del criterio 5), THEN THE Settings_Store SHALL segnalare all'utente un errore che indica la Soglia non valida, SHALL usare i valori predefiniti per tutte le Soglie e SHALL lasciare invariato il contenuto del file.
10. IF una scansione termina senza Scan_Sample oppure è interrotta prima del completamento, THEN THE Evaluator SHALL non assegnare alcun Verdetto e THE App SHALL indicare che la scansione non è valutabile.

### Requirement 12: Rilevamento cache e Cache_Defeat

**User Story:** Come utente, voglio che DiscCheck conosca la cache del mio drive, così che la verifica non legga dati dalla cache.

#### Acceptance Criteria

1. WHEN un Drive viene aggiunto alla tabella `drives`, THE Cache_Detector SHALL leggere con READ CD un blocco di 27 settori a un LBA X scelto all'interno della prima traccia, misurare il tempo di lettura T1 in millisecondi, rileggere subito lo stesso blocco misurando il tempo T2 e ripetere la coppia di letture 3 volte.
2. WHEN le 3 coppie di letture sono completate, THE Cache_Detector SHALL classificare il Drive come con cache audio se la mediana di T2 è minore o uguale al 50% della mediana di T1, altrimenti come senza cache audio.
3. WHEN il Drive è classificato con cache audio, THE Cache_Detector SHALL stimare la dimensione della cache leggendo tra la lettura e la rilettura del blocco di cui al criterio 1 un'area di 256 KB, 512 KB, 1.024 KB, 2.048 KB, 4.096 KB e 8.192 KB in ordine crescente, e SHALL assumere come dimensione stimata (`cache_kb`) la massima dimensione per cui la rilettura soddisfa ancora la condizione del criterio 2.
4. WHEN la classificazione e la stima sono completate, THE Repository SHALL salvare l'esito in `drives.caches_audio` (0 o 1) e `drives.cache_kb` (intero in KB, 0 se il Drive è classificato senza cache audio).
5. IF una lettura del Cache_Detector termina con status diverso da GOOD oppure il Disco inserito non è un CD audio con almeno 28.500 settori (circa 64 MB), THEN THE Cache_Detector SHALL interrompere il rilevamento, segnalare all'utente il motivo con LBA e sense dove disponibili, e THE Repository SHALL lasciare `drives.caches_audio` e `drives.cache_kb` senza valore (NULL).
6. WHEN è richiesto il Cache_Defeat, THE Verifier SHALL leggere, prima della rilettura del blocco, un'area di dimensione almeno pari a 2 volte `drives.cache_kb` (con minimo 4.096 KB) il cui primo settore disti almeno 64 MB (circa 28.500 settori) dal blocco da rileggere.
7. THE Verifier SHALL usare la lettura di un'area lontana come unico metodo di Cache_Defeat in v1, perché READ CD non dispone di FUA.
8. IF `drives.cache_kb` è NULL, THEN THE Verifier SHALL usare per il Cache_Defeat un'area di 4.096 KB.
9. IF il Disco non contiene un'area di Cache_Defeat con la distanza e la dimensione richieste dal criterio 6, THEN THE Verifier SHALL eseguire la rilettura senza Cache_Defeat e riportare nel report la limitazione, indicando il numero di settori mancanti.
10. WHERE il Drive è l'iHAS124, THE Cache_Detector SHALL confrontare `cache_kb` con il valore di riferimento di 2.048 KB e, IF la stima è fuori dall'intervallo 1.536–2.560 KB, THEN THE Cache_Detector SHALL registrare nel Log un avviso che riporta il valore stimato e quello atteso, senza modificare l'esito salvato.

### Requirement 13: Test di affidabilità dei puntatori C2

**User Story:** Come utente avanzato, voglio sapere se i C2 del mio drive sono affidabili, così che sappia quanto fidarmi della scansione standard.

#### Acceptance Criteria

1. WHEN l'utente avvia il C2_Reliability_Test, THE App SHALL richiedere la selezione di un Disco di prova con difetto noto e una conferma esplicita dell'utente prima di iniziare le letture.
2. WHEN l'utente conferma l'avvio del C2_Reliability_Test, THE Verifier SHALL leggere due volte con READ CD, con puntatori C2 attivi, la stessa area di 7.500 settori a partire dal primo settore audio del Disco. Prima di ciascuna lettura, THE Verifier SHALL applicare il Cache_Defeat. THE Verifier SHALL poi confrontare le due letture settore per settore, per individuare i settori con dati diversi.
3. WHEN il confronto ha individuato almeno 1 settore con dati diversi e ognuno di questi settori è segnalato dai puntatori C2 in almeno una delle due letture, THE Repository SHALL salvare `drives.c2_reliable=1`.
4. IF il confronto ha individuato almeno 1 settore con dati diversi non segnalato dai puntatori C2 in nessuna delle due letture, THEN THE Repository SHALL salvare `drives.c2_reliable=0` e THE App SHALL mostrare l'esito indicando il numero di settori con dati diversi non coperti dai C2.
5. IF `drives.c2_reliable=0`, THEN THE Verifier SHALL, all'avvio di ogni verifica, basarla solo sul confronto con la Sorgente e non usare i puntatori C2 per decidere le riletture.
6. IF il confronto non ha individuato alcun settore con dati diversi, THEN THE App SHALL mostrare un esito "non conclusivo" che indica di usare un Disco con un difetto noto, e THE Repository SHALL lasciare invariato `drives.c2_reliable`.
7. IF il C2_Reliability_Test termina con errore di lettura o è annullato dall'utente prima del completamento delle due letture, THEN THE Verifier SHALL interrompere il test, THE App SHALL mostrare lo stato di errore o annullato, e THE Repository SHALL lasciare invariato `drives.c2_reliable`.
8. IF il Drive selezionato non dichiara supporto C2 in Drive_Info, THEN THE App SHALL non avviare il C2_Reliability_Test e mostrare un messaggio che indica che il Drive non supporta i C2.

### Requirement 14: Verifica bit-perfect

**User Story:** Come utente, voglio verificare che il disco masterizzato sia identico ai file sorgente, così che sia certo che contiene l'audio originale.

#### Acceptance Criteria

1. WHEN l'utente avvia la verifica, THE App SHALL richiedere la selezione del Disco e della Sorgente (cartella o file CUE con file FLAC o WAV 16 bit / 44,1 kHz stereo) e SHALL consentire l'avvio solo dopo che entrambi sono stati selezionati.
2. WHEN la verifica parte, THE Verifier SHALL leggere la TOC e associare le tracce del Disco ai file della Sorgente per ordine: l'ordine dei file nel file CUE, oppure l'ordine alfabetico crescente dei nomi file se la Sorgente è una cartella.
3. IF la differenza assoluta tra il numero di campioni stereo di un file (588 campioni per settore) e la durata della traccia associata, espressa in campioni (settori × 588), supera il valore assoluto dell'offset in uso (Combined_Offset o Read_Offset inserito manualmente), THEN THE Verifier SHALL interrompere la verifica prima di iniziare l'estrazione, con un errore che indica il numero di traccia e le due durate in settori, e SHALL non produrre alcun esito per le tracce.
4. IF il numero di tracce del Disco è diverso dal numero di file della Sorgente, THEN THE Verifier SHALL interrompere la verifica prima di iniziare l'estrazione, con un errore che indica i due numeri, e SHALL non produrre alcun esito per le tracce.
5. WHEN la verifica è in corso, THE Verifier SHALL estrarre l'audio con READ CD e decodificare la Sorgente in PCM 16 bit stereo little-endian a 44.100 Hz.
6. IF una lettura restituisce C2 maggiori di 0, THEN THE Verifier SHALL rileggere il blocco con Cache_Defeat fino a un massimo di N tentativi, con N configurabile da 1 a 10 e valore predefinito 3.
7. WHEN le letture di una traccia sono completate, THE Verifier SHALL confrontare PCM letto e PCM sorgente campione per campione applicando l'offset in uso.
8. THE Verifier SHALL riportare per ogni traccia l'esito (identica o diversa), il numero di campioni stereo diversi e, se la traccia è diversa, l'indice base 0 del primo campione stereo diverso dall'inizio della traccia.
9. THE Verifier SHALL calcolare per ogni traccia CRC32 (System.IO.Hashing) e SHA-256 sia sul PCM letto sia sul PCM sorgente, e SHALL mostrarli in esadecimale nel report.
10. WHEN tutte le tracce del Disco sono state confrontate e tutte risultano identiche, THE App SHALL mostrare "Disco identico alla sorgente".
11. IF almeno una traccia è diversa oppure la verifica è terminata con errore oppure è stata annullata, THEN THE App SHALL non mostrare "Disco identico alla sorgente".
12. WHEN l'utente preme Annulla durante la verifica, THE Verifier SHALL interrompere la lettura, THE App SHALL mostrare lo stato annullato entro 5 secondi, e SHALL non salvare la verifica con risultato IDENTICAL.
13. WHILE la verifica è in corso, THE App SHALL mostrare l'avanzamento per traccia in percentuale da 0 a 100, aggiornato almeno una volta al secondo, e una tabella con numero traccia, esito, numero di campioni diversi e posizione della prima differenza per ogni traccia già confrontata.
14. FOR ALL PCM sorgente codificati e decodificati con il decoder scelto, la decodifica SHALL restituire campioni identici a quelli originali (round-trip del decoder su fixture FLAC/WAV).
15. THE Verifier SHALL usare il decoder FLAC scelto in D1 (CUETools.Codecs.FLAKE oppure `flac.exe` esterno).
16. IF un file della Sorgente non è leggibile, non è decodificabile, oppure non è 16 bit / 44,1 kHz stereo, THEN THE Verifier SHALL interrompere la verifica prima di iniziare l'estrazione, con un errore che indica il file e il motivo (non leggibile, non decodificabile o formato non supportato), e SHALL non produrre alcun esito per le tracce.
17. IF un comando READ CD fallisce con Sense_Data durante la verifica, THEN THE Verifier SHALL registrare i Sense_Data nel Log, interrompere la verifica con un errore che indica traccia, LBA e sense, mantenere gli esiti già calcolati per le tracce precedenti e salvare la verifica con risultato ERROR.
18. IF dopo N tentativi di rilettura un blocco restituisce ancora C2 maggiori di 0, THEN THE Verifier SHALL usare i dati dell'ultima lettura per il confronto, segnalare nel report il numero di traccia e l'LBA del blocco come lettura non affidabile e proseguire con il blocco successivo.

### Requirement 15: Gestione dell'offset di lettura

**User Story:** Come utente, voglio che DiscCheck gestisca l'offset del mio drive, così che il confronto con la sorgente non fallisca per un semplice scostamento.

#### Acceptance Criteria

1. WHEN il Read_Offset di un Drive viene determinato o inserito dall'utente, THE Repository SHALL salvare il valore intero, espresso in campioni, in `drives.read_offset`.
2. WHERE l'utente inserisce manualmente il Read_Offset, THE App SHALL accettare solo valori interi compresi tra -5000 e +5000 campioni inclusi e usare il valore inserito al posto di quello rilevato automaticamente.
3. IF il valore inserito manualmente non è un intero compreso tra -5000 e +5000 campioni, THEN THE App SHALL rifiutare il valore, mostrare un messaggio di errore che indica l'intervallo ammesso e conservare il Read_Offset precedente.
4. WHEN l'utente richiede il rilevamento automatico, THE Verifier SHALL cercare sulla prima traccia l'allineamento entro ±5000 campioni con cross-match esatto, cioè con 0 campioni diversi, su un blocco centrale di 44.100 campioni stereo consecutivi (1 secondo) della traccia.
5. WHEN l'allineamento è trovato, THE Verifier SHALL applicare lo stesso offset (Combined_Offset) a tutte le tracce del Disco e mostrare all'utente il valore rilevato in campioni.
6. IF nessun allineamento esatto è trovato entro ±5000 campioni, THEN THE Verifier SHALL terminare il rilevamento senza modificare `drives.read_offset`, mostrare un messaggio di errore che indica che l'offset non è stato rilevato e richiedere l'inserimento manuale.
7. IF la prima traccia ha una durata inferiore a 44.100 + 10.000 campioni stereo, THEN THE Verifier SHALL terminare il rilevamento senza modificare `drives.read_offset` e segnalare che la traccia è troppo breve per il rilevamento automatico.
8. WHEN il confronto viene eseguito con un offset k diverso da zero, THE Verifier SHALL escludere dal confronto, per ogni traccia, esattamente |k| campioni di bordo non leggibili.
9. WHEN i campioni di bordo sono stati esclusi, THE Verifier SHALL indicare nel report il numero di campioni esclusi per ciascuna traccia.
10. WHEN una verifica viene completata, THE Verifier SHALL salvare in `verifications.offset_used` l'offset effettivamente usato, in campioni.
11. THE Verifier SHALL usare la Sorgente locale come riferimento unico e non usare AccurateRip né il database CUETools.
12. FOR ALL segnali PCM con offset artificiale k intero compreso in ±5000 campioni, il rilevamento SHALL restituire k (proprietà su dati sintetici).

### Requirement 16: Database SQLite e migrazioni

**User Story:** Come utente, voglio un archivio locale dei risultati, così che possa consultarli e confrontarli nel tempo.

#### Acceptance Criteria

1. THE Database SHALL risiedere per impostazione predefinita in `%LOCALAPPDATA%\DiscCheck\disccheck.db`.
2. WHERE l'utente ha impostato un percorso alternativo per il Database nelle impostazioni, THE App SHALL usare tale percorso al successivo avvio al posto di quello predefinito.
3. THE Database SHALL contenere le tabelle `schema_version`, `drives`, `media`, `discs`, `scans`, `scan_samples`, `verifications`, `verification_tracks` con le colonne definite nella specifica (§9).
4. THE `scan_samples` SHALL avere chiave primaria (`scan_id`, `t_sec`), essere `WITHOUT ROWID` e usare `ON DELETE CASCADE` su `scans`, in modo che l'eliminazione di una scansione elimini tutti i suoi Scan_Sample.
5. THE `verification_tracks` SHALL avere chiave primaria (`verification_id`, `track_no`).
6. THE `drives` SHALL avere vincolo UNIQUE su (`vendor`, `model`, `firmware`).
7. THE Migrator SHALL applicare script SQL versionati in ordine crescente di versione e aggiornare `schema_version` al numero dell'ultimo script applicato, senza Entity Framework.
8. WHEN l'App viene avviata, THE Migrator SHALL creare il Database se assente e applicare le migrazioni mancanti.
9. IF l'applicazione di uno script di migrazione fallisce, THEN THE Migrator SHALL eseguire il rollback dello script in errore, lasciare `schema_version` al valore precedente, interrompere l'avvio del Database e l'App SHALL mostrare un messaggio di errore che indica la versione dello script fallito.
10. IF il Database non può essere creato o aperto (percorso non scrivibile, file bloccato o file non SQLite), THEN THE App SHALL mostrare un messaggio di errore che indica il percorso e la causa e SHALL NOT modificare il file esistente.
11. WHEN una scansione termina, THE Repository SHALL inserire tutti gli Scan_Sample in un'unica transazione (circa 4.800 righe per un CD da 80 minuti) e completare il salvataggio entro 2 secondi su un CD da 80 minuti.
12. IF l'inserimento della transazione fallisce, THEN THE Repository SHALL eseguire il rollback senza lasciare righe parziali in `scans` o `scan_samples`, e l'App SHALL segnalare l'errore mantenendo gli Scan_Sample in memoria per un nuovo salvataggio.
13. THE `scans.verdict` SHALL ammettere solo i valori OK, WARN, KO, `scans.scanner` solo i valori StandardC2 e LiteOn, `verifications.result` solo i valori IDENTICAL, DIFFERENT, ERROR, e il Database SHALL rifiutare qualunque altro valore.
14. WHEN il Disco viene salvato, THE Repository SHALL registrare label, media, drive di masterizzazione, velocità e data di masterizzazione, software, `toc_hash`, `track_count`, `total_sectors`, `storage_notes` e `recheck_due`.
15. IF il Database ha una `schema_version` superiore alla versione massima nota all'App, THEN THE Migrator SHALL rifiutare l'apertura senza modificare il Database e l'App SHALL mostrare un messaggio che indica la versione trovata, la versione massima supportata e l'incompatibilità.
16. FOR ALL entità salvate, la lettura dopo il salvataggio SHALL restituire un oggetto equivalente (round-trip dei Repository).

### Requirement 17: Storico

**User Story:** Come utente, voglio consultare lo storico di dischi, scansioni e verifiche con filtri, così che possa ritrovare i risultati.

#### Acceptance Criteria

1. WHEN l'utente apre la vista Storico, THE App SHALL elencare dal Database dischi, scansioni e verifiche in tre elenchi distinti, ciascuno ordinato per data decrescente, e completare la visualizzazione entro 2 secondi con fino a 10.000 record per elenco.
2. WHEN l'utente imposta o modifica uno o più filtri tra supporto, velocità, intervallo di date (estremi inclusi, giorno locale) e Verdetto (uno o più valori tra OK, WARN, KO), THE App SHALL mostrare solo i record che soddisfano tutti i filtri impostati (combinazione AND) e aggiornare gli elenchi entro 1 secondo.
3. WHEN un filtro riguarda un attributo non presente in un elenco (ad esempio il Verdetto per le verifiche), THE App SHALL lasciare quell'elenco non filtrato da quel criterio.
4. WHEN l'utente seleziona una scansione, THE App SHALL mostrare entro 2 secondi il riepilogo (C1 totale, medio e massimo, C2 totale e massimo, CU totale), il grafico con tutti gli Scan_Sample salvati e il Verdetto della scansione.
5. WHEN l'utente seleziona una verifica, THE App SHALL mostrare la tabella degli esiti con una riga per ciascuna traccia, contenente numero traccia, CRC32 (8 cifre esadecimali), SHA-256 (64 cifre esadecimali) ed esito.
6. WHEN l'utente conferma l'eliminazione di una scansione dopo una richiesta di conferma, THE Repository SHALL eliminare la scansione e tutti i relativi Scan_Sample in un'unica transazione, e THE App SHALL rimuovere la scansione dall'elenco.
7. IF l'eliminazione di una scansione fallisce, THEN THE Repository SHALL eseguire il rollback lasciando invariati scansione e Scan_Sample, e THE App SHALL mostrare un messaggio di errore che indica il fallimento dell'eliminazione mantenendo la scansione nell'elenco.
8. WHEN i filtri non producono risultati, THE App SHALL mostrare un elenco vuoto con un messaggio informativo che indica che nessun record corrisponde ai filtri.
9. IF la lettura dal Database fallisce all'apertura della vista Storico o all'applicazione di un filtro, THEN THE App SHALL mostrare un messaggio di errore che indica l'impossibilità di leggere lo storico e mantenere visibile l'ultimo elenco caricato correttamente.

### Requirement 18: Grafici e vista Scansione

**User Story:** Come utente, voglio un grafico interattivo live durante la scansione, così che possa seguire la qualità del disco in tempo reale.

#### Acceptance Criteria

1. WHILE una scansione è in corso, WHEN l'App consuma uno Scan_Sample dallo Scan_Channel, THE App SHALL aggiungere il campione al grafico entro 500 ms, con tempo sull'asse X (valori in secondi, etichette nel formato mm:ss secondo il Requirement 38), C1 come barre blu, C2 come barre rosse, CU come barre viola (valori per campione sull'asse Y sinistro) e SpeedX come linea grigia (asse Y destro, in X), con colori letti dai Design_Token `Das.Color.Chart.C1`, `Das.Color.Chart.C2`, `Das.Color.Chart.CU` e `Das.Color.Chart.Speed` (Requirement 29) e non definiti nel codice dell'App, omettendo dal disegno i valori nulli (ad esempio C1 e CU per lo StandardC2Scanner).
2. WHEN l'utente ruota la rotella del mouse sul grafico o trascina il grafico con il pulsante sinistro premuto, THE App SHALL eseguire rispettivamente zoom e spostamento (ScottPlot.Avalonia 5.x) e aggiornare la vista entro 100 ms, senza modificare né perdere i dati dei campioni.
3. WHEN una scansione termina con successo, THE App SHALL mostrare entro 1 secondo il riepilogo con C1 totale (somma dei campioni), C1 medio (media aritmetica dei campioni con C1 non nullo, con 2 decimali), C1 massimo, C2 totale, C2 massimo e CU totale, insieme al Verdetto calcolato dall'Evaluator, e SHALL mostrare "n/d" per ogni valore non prodotto dallo scanner usato.
4. WHILE nessuna scansione è in corso, THE App SHALL consentire di scegliere lo scanner tra i Quality_Scanner con Probe positivo per il Drive selezionato e la velocità tra quelle dichiarate in Drive_Info, e SHALL abilitare il comando Avvia solo se sono presenti un Disco, uno scanner e una velocità selezionati.
5. WHEN l'utente attiva Avvia, THE App SHALL azzerare grafico e riepilogo della scansione precedente, disabilitare Avvia e la scelta di scanner e velocità, abilitare Annulla e mantenere questo stato fino al termine, all'annullamento o al fallimento della scansione.
6. WHEN il numero di campioni nell'intervallo visibile supera il numero di pixel di larghezza del grafico, THE App SHALL ridurre i punti disegnati preservando i valori massimi di ogni intervallo, in modo che nessun picco di C1, C2 o CU scompaia, e SHALL mantenere il thread UI libero, senza blocchi superiori a 100 ms.
7. WHEN l'utente attiva Annulla durante una scansione, THE App SHALL interrompere la scansione entro 2 secondi, conservare nel grafico i campioni già acquisiti, non mostrare il Verdetto, indicare lo stato "annullata", riabilitare Avvia e la scelta di scanner e velocità e disabilitare Annulla.
8. IF una scansione fallisce (errore SCSI, Probe non riuscito o Drive rimosso), THEN THE App SHALL interrompere la scansione, mostrare un messaggio di errore che indica la causa, conservare nel grafico i campioni già acquisiti, non mostrare il Verdetto, riabilitare Avvia e disabilitare Annulla.
9. IF il Drive selezionato non ha un Disco inserito, THEN THE App SHALL mantenere Avvia disabilitato e mostrare un messaggio che richiede di inserire un Disco.
10. THE App SHALL mostrare il grafico della vista Scansione esclusivamente tramite il Chart_View, con Chart_Style, Chart_Cursor, Chart_Selection, linee soglia ed evidenziazione degli errori definiti nei Requirement 38 e 39, e con gli obiettivi di prestazione del Requirement 38 criterio 14.

### Requirement 19: Confronto tra scansioni

**User Story:** Come utente, voglio sovrapporre più scansioni, così che possa confrontare velocità o supporti diversi.

#### Acceptance Criteria

1. WHEN l'utente seleziona da 2 a 8 scansioni, THE Comparator SHALL sovrapporre nello stesso grafico una curva per scansione, con il tempo in secondi sull'asse X e la metrica scelta tra C1, C2 e CU sull'asse Y (predefinita C1). Ogni curva SHALL avere un colore diverso dalle altre e una voce di legenda che riporta data della scansione, supporto e velocità.
2. IF l'utente seleziona più di 8 scansioni, THEN THE App SHALL mantenere selezionate le prime 8 e mostrare un messaggio che indica il limite massimo di 8 scansioni confrontabili.
3. IF le scansioni selezionate hanno Drive diversi o velocità diverse, THEN THE App SHALL mostrare l'avviso "Confronta solo scansioni fatte con lo stesso drive alla stessa velocità", e SHALL mantenere le curve sovrapposte.
4. WHEN l'utente seleziona due o più scansioni, THE Comparator SHALL mostrare una tabella con una riga per scansione e le colonne C1 medio (2 decimali), C1 massimo, C2 totale, CU totale e Verdetto (OK, WARN o KO). Gli Scan_Sample con C1, C2 o CU nulli SHALL essere esclusi dal calcolo del valore corrispondente.
5. IF l'utente seleziona una sola scansione, THEN THE App SHALL mostrare un messaggio che richiede almeno due scansioni e SHALL non eseguire la sovrapposizione.
6. WHERE l'utente avvia il wizard "ricerca velocità ottimale", THE Comparator SHALL leggere le velocità supportate da Drive_Info, compresa la velocità minima supportata dall'iHAS124, e proporre solo quelle comprese tra la minima supportata e 12x incluse, per dischi con velocità nominale 52x. Se nessuna velocità supportata rientra nell'intervallo da 4x a 12x, THE Comparator SHALL proporre la velocità minima supportata.
7. IF Drive_Info non fornisce alcuna velocità supportata, THEN THE App SHALL non proporre velocità e mostrare un messaggio che indica l'impossibilità di determinarle, lasciando invariata la selezione corrente.
8. THE App SHALL mostrare il grafico della vista Confronto esclusivamente tramite il Chart_View, con le curve distinguibili anche per stile di linea e marcatore (Requirement 38 criterio 12) e con un Chart_Cursor il cui tooltip riporta il valore della metrica scelta per ciascuna curva visibile.

### Requirement 20: Archivio e monitoraggio del degrado

**User Story:** Come utente, voglio monitorare il degrado dei miei dischi nel tempo e ricevere promemoria, così che possa copiarli prima di perdere dati.

#### Acceptance Criteria

1. WHEN l'utente apre la vista Archivio/Degrado per un Disco con almeno una scansione salvata, THE Degradation_Monitor SHALL mostrare entro 2 secondi un grafico di trend con un punto per ogni scansione del Disco, ordinato per data di scansione crescente, per ciascuna delle tre serie: C1 medio, C1 massimo e C2 totale.
2. THE Repository SHALL salvare `discs.recheck_due` come data di calendario (senza orario) per ogni Disco con almeno una scansione, e `NULL` per un Disco senza scansioni.
3. WHEN una scansione viene salvata per un Disco, THE Degradation_Monitor SHALL impostare `recheck_due` alla data della scansione più l'intervallo di ricontrollo configurato nel Settings_Store (valore predefinito: 5 anni), senza modificare il `recheck_due` degli altri Dischi.
4. THE Settings_Store SHALL rendere configurabile l'intervallo di ricontrollo come numero intero di anni compreso tra 5 e 10 inclusi.
5. WHEN l'utente apre la vista Archivio/Degrado, THE App SHALL elencare i Dischi con `recheck_due` anteriore o uguale alla data odierna (scaduto) o compreso nei 90 giorni successivi alla data odierna (in scadenza), ordinati per `recheck_due` crescente, indicando per ciascuno lo stato "scaduto" o "in scadenza".
6. WHEN l'App viene avviata e almeno un Disco ha `recheck_due` anteriore o uguale alla data odierna, THE App SHALL mostrare una sola volta per avvio un promemoria che riporta il numero di Dischi scaduti e consente di aprire la vista Archivio/Degrado.
7. WHEN l'utente apre la vista Archivio/Degrado per un Disco con almeno due scansioni, e il Verdetto dell'ultima scansione è peggiore di quello della scansione precedente (ordine dal migliore al peggiore: OK, WARN, KO), THE App SHALL evidenziare il Disco come "in peggioramento".
8. THE App SHALL mostrare nella vista Archivio/Degrado i seguenti suggerimenti di conservazione: posizione verticale, jewel case, umidità relativa tra 20% e 50%, temperatura tra −10 °C e 23 °C e mai sopra 32 °C, riparo da luce e inquinanti.
9. THE App SHALL mostrare nella vista Archivio/Degrado l'indicazione di longevità per tipo di supporto (ftalocianina+oro oltre 100 anni, ftalocianina+argento da 50 a 100 anni, cianina o AZO+argento da 20 a 50 anni) e l'obiettivo di BLER iniziale inferiore a 50.
10. IF un Disco ha una sola scansione, THEN THE App SHALL mostrare il trend con un solo punto per ciascuna serie e nessuna evidenziazione "in peggioramento".
11. IF un Disco non ha scansioni salvate, THEN THE App SHALL mostrare nella vista Archivio/Degrado un messaggio informativo che indica l'assenza di scansioni, senza grafico di trend e senza impostare `recheck_due`.
12. IF il valore dell'intervallo di ricontrollo in `settings.json` non è un intero compreso tra 5 e 10, THEN THE Settings_Store SHALL segnalare l'errore e usare il valore predefinito di 5 anni.
13. IF la lettura dei dati dal Repository fallisce all'apertura della vista Archivio/Degrado, THEN THE App SHALL mostrare un messaggio di errore che indica l'impossibilità di caricare i dati e non SHALL mostrare trend né elenco parziali.
14. THE App SHALL mostrare il grafico di trend della vista Archivio/Degrado esclusivamente tramite il Chart_View (Requirement 38), con asse X in date locali e asse Y con etichetta e unità della serie.

### Requirement 21: Export

**User Story:** Come utente, voglio esportare dati e grafici, così che possa condividerli o archiviarli fuori dall'applicazione.

#### Acceptance Criteria

1. WHEN l'utente richiede l'export CSV di una scansione e sceglie un percorso di destinazione, THE Exporter SHALL scrivere un file CSV in codifica UTF-8 contenente una riga di intestazione e una riga per ciascuno Scan_Sample, con le colonne nell'ordine: tempo in secondi (`TimeSeconds`), C1, C2, CU, velocità (`SpeedX`); i valori nulli di C1 o `SpeedX` SHALL essere scritti come campo vuoto.
2. WHEN l'utente richiede l'export PNG, THE Exporter SHALL salvare il grafico corrente, con gli stessi dati, zoom e serie visibili al momento della richiesta, applicando il Chart_Style della variante di tema attiva, a scala 2x (larghezza minima 2.400 pixel e altezza minima 1.200 pixel, con la dimensione logica del Chart_View di almeno 1.200 × 600) come immagine PNG.
3. WHEN l'utente richiede il report HTML di una scansione, THE Exporter SHALL generare un unico file HTML autosufficiente (apribile senza connessione di rete e senza file aggiuntivi) contenente: dati del Disco e del Drive, Verdetto, valori riepilogativi (massimo e media di C1, C2, CU), il grafico della scansione come immagine incorporata e, se presente, l'esito della verifica.
4. WHERE un report HTML include una verifica, THE Exporter SHALL includere nel report il Read_Offset e il Combined_Offset usati, il numero di campioni di bordo esclusi e l'esito della verifica per ciascuna traccia.
5. WHEN un export (CSV, PNG o HTML) termina con successo, THE App SHALL mostrare entro 2 secondi un messaggio di conferma con il percorso completo del file creato.
6. IF il file di destinazione esiste già, THEN THE App SHALL chiedere conferma all'utente prima di sovrascriverlo e, se l'utente rifiuta, THE Exporter SHALL non modificare il file esistente e non avviare l'export.
7. IF la scrittura del file di export fallisce (percorso non valido, permessi insufficienti, spazio su disco esaurito o file in uso), THEN THE Exporter SHALL mostrare un messaggio di errore che indica la causa del fallimento e il percorso di destinazione, SHALL eliminare l'eventuale file parziale creato e SHALL lasciare inalterati la scansione e i dati nel Database.
8. IF la scansione selezionata non contiene alcuno Scan_Sample, THEN THE Exporter SHALL non creare alcun file e THE App SHALL mostrare un messaggio che indica che non ci sono dati da esportare.
9. THE Exporter SHALL codificare il CSV usando la virgola come separatore di campo, il punto come separatore decimale, nessun separatore delle migliaia e il carattere di nuova riga `\r\n`, indipendentemente dalla cultura locale del sistema.
10. FOR ALL scansioni, l'export CSV seguito dall'importazione del CSV SHALL restituire gli stessi valori numerici di C1, C2, CU e `SpeedX`, con `TimeSeconds` identico, e lo stesso numero di righe (round-trip CSV).
11. WHEN l'utente richiede l'export SVG del grafico corrente, THE Exporter SHALL scrivere un file SVG vettoriale (XML valido, senza immagini raster incorporate per le serie di dati) con gli stessi dati, zoom e serie visibili, il Chart_Style della variante di tema attiva e testi in formato testo, di dimensione logica pari a quella del Chart_View e minima 1.200 × 600 unità, usando l'esportazione SVG di ScottPlot 5.
12. THE Exporter SHALL scrivere nei file CSV, PNG e SVG numeri con la cultura invariante (punto decimale, nessun separatore delle migliaia), indipendentemente dalla cultura usata nella UI (Requirement 38 criterio 9).

### Requirement 22: Impostazioni

**User Story:** Come utente, voglio configurare soglie e comportamento, così che l'app si adatti al mio flusso di lavoro.

#### Acceptance Criteria

1. THE App SHALL offrire una vista Impostazioni che permetta di modificare: Soglie, velocità predefinita di scansione (valore intero da 1x a 52x), variante Lite-On (Metodo_Nuovo oppure Metodo_Vecchio, predefinita Metodo_Nuovo), percorso del Database, attivazione della Modalità_Debug (predefinita disattivata) e tema.
2. WHEN l'utente salva impostazioni con tutti i valori validi, THE Settings_Store SHALL scrivere `settings.json` entro 1 secondo e THE App SHALL mostrare una conferma di salvataggio avvenuto.
3. WHEN l'App viene avviata e `settings.json` è presente e valido, THE Settings_Store SHALL caricare i valori in esso contenuti prima che la vista principale sia utilizzabile.
4. THE Settings_Store SHALL rendere configurabili il numero massimo N di tentativi di rilettura della verifica (intero da 1 a 10, predefinito 3) e il timeout SCSI per singolo comando (intero da 1 a 300 secondi, predefinito 30).
5. WHEN l'utente attiva o disattiva la Modalità_Debug, THE Log SHALL abilitare o disabilitare il livello Verbose dei CDB entro 1 secondo, senza riavvio dell'App e anche per le scansioni in corso.
6. WHEN l'utente cambia il percorso del Database, THE App SHALL richiedere conferma e, se l'utente conferma, chiudere l'accesso ai dati sul percorso precedente e riaprirlo sul nuovo percorso, creando il Database e applicando il Migrator se il file non esiste; se l'utente annulla, THE App SHALL mantenere il percorso precedente.
7. WHEN l'utente seleziona il tema Fluent chiaro o scuro, THE App SHALL applicare il tema entro 1 secondo senza riavvio, commutando la variante dei Design_Token di DAS_Design (Requirement 29) e senza sostituire alcun colore nelle viste, e THE Settings_Store SHALL ripristinarlo al successivo avvio; il tema predefinito è chiaro.
8. FOR ALL oggetti Settings validi, la serializzazione seguita dalla deserializzazione SHALL produrre un oggetto con tutti i campi uguali all'originale (round-trip JSON).
9. IF l'utente salva impostazioni contenenti un valore fuori dai limiti (velocità, N, timeout SCSI, soglie) o un percorso del Database vuoto, THEN THE App SHALL rifiutare il salvataggio, indicare il campo non valido e il vincolo violato, e THE Settings_Store SHALL lasciare invariato `settings.json`.
10. IF all'avvio `settings.json` è assente, THEN THE Settings_Store SHALL creare il file con i valori predefiniti; IF `settings.json` è presente ma non leggibile o non valido, THEN THE Settings_Store SHALL usare i valori predefiniti, THE App SHALL mostrare un avviso e il file originale SHALL restare inalterato fino al successivo salvataggio da parte dell'utente.
11. IF il nuovo percorso del Database non è scrivibile o il Database non può essere aperto su di esso, THEN THE App SHALL mantenere il percorso e l'accesso ai dati precedenti, mostrare un messaggio di errore che indica la causa e non modificare `settings.json`.

### Requirement 23: Strategia di test

**User Story:** Come sviluppatore, voglio una suite di test che copra il codice senza hardware e con hardware opzionale, così che possa evolvere il progetto in sicurezza.

#### Acceptance Criteria

1. THE DiscCheck SHALL usare xUnit, FluentAssertions e NSubstitute come unici framework di test, di asserzione e di mock in tutti i progetti di test (Core.Tests, Scsi.Tests, Hardware.Tests, DAS.Design.Tests).
2. THE Core.Tests e Scsi.Tests SHALL includere almeno un test automatico per ciascuna delle aree: parsing di TOC, parsing di ATIP, parsing di Sense_Data, conteggio dei bit C2, valutazione del Verdetto (almeno un caso per ciascun esito OK, WARN e KO), allineamento del Combined_Offset e Repository.
3. THE Scsi.Tests SHALL usare Fake_Transport con risposte registrate in file `.bin` in `tests/Fixtures`, senza accesso a Drive fisici.
4. THE Hardware.Tests SHALL essere marcati con `[Trait("Category","Hardware")]`, e nessun test che richiede un Drive fisico SHALL essere privo di tale marcatura.
5. WHEN viene eseguito `dotnet test --filter "Category!=Hardware"` su una macchina senza Drive ottici, THE DiscCheck SHALL eseguire tutti i test non marcati Hardware con 0 test falliti e 0 test saltati per mancanza di hardware.
6. IF una pull request introduce un nuovo comando SCSI senza almeno un test Scsi.Tests con Fake_Transport e risposta registrata in `tests/Fixtures`, THEN THE DiscCheck SHALL considerare la modifica non conforme e non approvabile.
7. WHEN il LiteOnScanner scansiona lo stesso Disco, sullo stesso Drive e alla stessa velocità nominale usata da QPxTool o Opti Drive Control, THE DiscCheck SHALL produrre un totale C1 compreso entro ±10% del totale di riferimento e un totale C2 uguale al totale di riferimento (validazione cross-check hardware, eseguita come Hardware.Tests).
8. IF una scansione vendor termina per completamento, errore o cancellazione, THEN THE DiscCheck SHALL inviare il comando di fine scansione e chiudere il Test_Mode in un blocco `finally`.
9. WHEN l'utente richiede la cancellazione di un'operazione hardware, THE DiscCheck SHALL interrompere l'operazione e restituire il controllo entro 2 secondi, lasciando il Drive fuori dal Test_Mode.

### Requirement 24: Limiti di scope v1

**User Story:** Come responsabile del progetto, voglio confini espliciti per la v1, così che non vengano implementate funzioni previste per versioni successive.

#### Acceptance Criteria

1. THE DiscCheck SHALL escludere dalla v1 la masterizzazione (prevista in v2 con WRITE in DAO e CUE), senza esporre nell'App alcun comando, vista o opzione di scrittura su Disco e senza inviare al Drive comandi MMC di scrittura (WRITE).
2. THE DiscCheck SHALL escludere dalla v1 la conversione hi-res→16/44,1, senza esporre nell'App alcun comando o opzione di conversione.
3. THE DiscCheck SHALL escludere dalla v1 le scansioni di DVD e BD, senza esporre nell'App alcun comando di scansione, verifica o lettura TOC/ATIP eseguibile su questi supporti.
4. THE DiscCheck SHALL supportare in v1 solo Windows 10 e Windows 11 x64, con `IScsiTransport` come unico punto di astrazione del trasporto, così che un trasporto Linux/macOS basato su SG_IO (v2) possa essere aggiunto senza modificare Core e App.
5. THE DiscCheck SHALL escludere dalla v1 l'implementazione di Quality_Scanner per Drive non Lite-On (oltre a StandardC2Scanner), mantenendo l'interfaccia `IQualityScanner` predisposta come da Requirement 6.
6. THE DiscCheck SHALL escludere dalla v1 la misura del jitter e il rilevamento del buffer underrun in scrittura (A.7, A.8), senza esporre nell'App alcun comando, colonna o grafico relativo a tali misure.
7. WHEN il Drive selezionato riporta un DVD o un BD inserito, THE App SHALL mostrare entro 5 secondi un messaggio che indica che il tipo di disco non è supportato in v1.
8. IF l'App mostra il messaggio di tipo di disco non supportato, THEN THE App SHALL disabilitare i comandi di scansione e di verifica per quel Drive, non inviare comandi di scansione o di lettura dati al Drive e lasciare invariati il Database e le impostazioni.

### Requirement 25: Decisioni aperte e licenza

**User Story:** Come responsabile del progetto, voglio che le decisioni aperte siano tracciate, così che siano risolte prima delle milestone che le richiedono.

#### Acceptance Criteria

1. THE DiscCheck SHALL mantenere un registro delle decisioni in cui ciascuna decisione D1, D2, D3, D4 e D5 riporta: identificativo, opzioni considerate, stato ("aperta" o "risolta"), opzione scelta, data di risoluzione (AAAA-MM-GG) e motivazione di almeno una frase.
2. WHEN inizia l'implementazione della milestone M5, THE DiscCheck SHALL avere la decisione D1 (decoder FLAC: CUETools.Codecs.FLAKE LGPL oppure `flac.exe` esterno) nello stato "risolta".
3. WHEN inizia l'implementazione della milestone M6, THE DiscCheck SHALL avere la decisione D2 (licenza: GPL-2.0 obbligatoria se si distribuisce il porting di QPxTool_Source, oppure uso personale) nello stato "risolta".
4. WHEN viene preparato il primo rilascio, THE DiscCheck SHALL avere la decisione D3 (nome definitivo dell'applicativo) nello stato "risolta".
5. THE DiscCheck SHALL implementare nella v1 zero funzioni di masterizzazione, qualunque sia lo stato o l'esito della decisione D4 (masterizzazione integrata in v2: sì o no).
6. IF all'inizio della milestone M5 la decisione D1 oppure, all'inizio della milestone M6, la decisione D2 è nello stato "aperta", THEN THE DiscCheck SHALL non avviare l'implementazione di quella milestone e SHALL segnalare nel registro delle decisioni quale decisione è bloccante.
7. WHERE il codice portato da QPxTool_Source è incluso nel progetto, THE DiscCheck SHALL riportare in ogni file portato un'intestazione con il nome del file originale di QPxTool_Source, l'avviso di copyright originale, l'identificativo di licenza "GPL-2.0" e l'indicazione che il file è stato modificato.
8. WHERE la decisione D2 è risolta con la distribuzione del porting di QPxTool_Source, THE DiscCheck SHALL includere in ogni distribuzione il testo completo della licenza GPL-2.0.
9. THE DiscCheck SHALL accedere al decoder FLAC (decisione D1) solo tramite un'interfaccia di decodifica definita nel Core, così che il Verifier non contenga alcun riferimento diretto a CUETools.Codecs.FLAKE né a `flac.exe` e il cambio di decoder non richieda modifiche al codice del Verifier.
10. THE DiscCheck SHALL mantenere nel registro delle decisioni la decisione D5 (licenza degli asset di DAS_Design: stessa licenza del progetto, CC-BY 4.0 per gli asset grafici con licenza del progetto per il codice, oppure CC0 1.0), con le regole di risoluzione del Requirement 36.

### Requirement 26: Gestione dei rischi

**User Story:** Come sviluppatore, voglio mitigazioni esplicite per i rischi noti, così che i fallimenti hardware siano gestiti in modo prevedibile.

#### Acceptance Criteria

1. WHEN la milestone M1 è completata, THE DiscCheck SHALL aver eseguito almeno un comando vendor di QPxTool_Source tramite il bridge ICY BOX IB-525-U3 e aver documentato l'esito come "accettato" (il Drive risponde con GOOD status) oppure "rifiutato" (il bridge o il Drive restituisce Sense_Data di errore o un timeout), riportando per il caso "rifiutato" Sense_Data (Key, ASC, ASCQ) o la causa del timeout.
2. IF il Drive restituisce Sense_Data di errore o va in timeout (30 secondi) al primo comando vendor del Metodo_Nuovo, THEN THE LiteOnScanner SHALL ritentare la scansione con il Metodo_Vecchio una sola volta, senza intervento dell'utente.
3. IF anche il tentativo con il Metodo_Vecchio fallisce, THEN THE App SHALL mostrare un messaggio di errore che indica che il firmware del Drive non è compatibile, proporre all'utente il fallback a StandardC2Scanner (accetta/rifiuta) e non avviare StandardC2Scanner senza conferma.
4. WHEN viene mostrato un risultato LiteOnScanner su un Drive con chipset MediaTek, THE App SHALL mostrare accanto al risultato un avviso testuale che indica che C1 può essere sottostimato e che per il Verdetto KO contano C2 e CU.
5. IF all'apertura della vista Drive il Drive risulta ancora in Test_Mode per un arresto anomalo precedente (comando di fine scansione non completato), THEN THE App SHALL abilitare il comando "Reset drive" nella vista Drive, e WHEN l'utente lo esegue, THE App SHALL inviare il comando di fine scansione e indicare entro 10 secondi l'esito (riuscito o fallito con causa).
6. IF il comando "Reset drive" fallisce, THEN THE App SHALL mantenere abilitato il comando "Reset drive", mostrare un messaggio che indica di espellere e reinserire il disco o di ricollegare il Drive, e registrare l'errore nel Log.
7. IF all'avvio l'App non dispone di privilegi amministrativi, THEN THE App SHALL mostrare un messaggio che indica che sono richiesti privilegi di amministratore e le istruzioni per riavviare l'App come amministratore, e SHALL disabilitare l'avvio delle scansioni e della verifica finché i privilegi non sono disponibili.

### Requirement 27: Piano per milestone

**User Story:** Come responsabile del progetto, voglio che il lavoro sia consegnato per milestone verificabili.

#### Acceptance Criteria

1. WHEN viene dichiarata completata la milestone M0, THE DiscCheck SHALL soddisfare tutte le condizioni seguenti: la soluzione `DiscCheck.sln` compila senza errori con tutti i progetti del Requirement 1; l'App risolve i servizi tramite dependency injection; il Log scrive su file e su console tramite Serilog; il file `CLAUDE.md` è presente nella radice del repository; l'App si avvia e mostra una finestra principale priva di contenuto funzionale; il progetto `DAS.Design` compila con Design_Token, Theme_Entry_Point `DasTheme` e DAS_Logo (lockup, variante per lo sfondo del tema attivo) e la finestra principale applica il tema tramite `DasTheme` e mostra il DAS_Logo nell'intestazione secondo il Requirement 32; i test del Requirement 37 relativi a token, build e indipendenza di DAS_Design sono superati.
2. WHEN viene dichiarata completata la milestone M1, THE DiscCheck SHALL soddisfare tutte le condizioni seguenti: WinScsiPassThrough esegue INQUIRY e MODE SENSE 0x2A; il Drive_Enumerator elenca i Drive rilevati; con il Drive di riferimento collegato via USB (ICY BOX IB-525-U3) l'App mostra il modello "LITE-ON iHAS124", la versione firmware e l'indicazione di supporto C2 (sì/no).
3. WHEN viene dichiarata completata la milestone M2, THE DiscCheck SHALL soddisfare tutte le condizioni seguenti: READ TOC restituisce numero di tracce, LBA di inizio di ogni traccia e LBA del lead-out; la lettura ATIP produce il Media_ID e il produttore corrispondente dalla ATIP_Table; la vista Disco mostra TOC, Media_ID e produttore per un CD audio inserito.
4. WHEN viene dichiarata completata la milestone M3, THE DiscCheck SHALL soddisfare tutte le condizioni seguenti: StandardC2Scanner esegue una scansione completa su un Drive che dichiara supporto C2; il grafico live aggiunge un Scan_Sample ogni secondo di disco durante la scansione; al termine l'Evaluator calcola il Verdetto (OK, WARN o KO) e l'App lo mostra.
5. WHEN viene dichiarata completata la milestone M4, THE DiscCheck SHALL soddisfare tutte le condizioni seguenti: il Migrator crea il Database e imposta `schema_version`; il Repository salva e rilegge una scansione con i suoi Scan_Sample senza perdita di dati; la vista Storico elenca le scansioni salvate.
6. WHEN viene dichiarata completata la milestone M5, THE DiscCheck SHALL soddisfare tutte le condizioni seguenti: il Cache_Detector rileva presenza e dimensione della cache audio del Drive; il Cache_Defeat evita le risposte dalla cache durante la rilettura; il Verifier confronta Disco e Sorgente e riporta l'esito bit-perfect dopo aver applicato il Combined_Offset rilevato.
7. WHEN viene dichiarata completata la milestone M6, THE DiscCheck SHALL soddisfare tutte le condizioni seguenti: LiteOnScanner esegue una scansione completa su un Drive Lite-On producendo Scan_Sample con C1, C2 e CU; al termine della scansione il Drive esce dal Test_Mode con il comando di fine scansione.
8. WHEN viene dichiarata completata la milestone M7, THE DiscCheck SHALL soddisfare tutte le condizioni seguenti: il Comparator sovrappone almeno 2 scansioni; l'Exporter produce export CSV, PNG e report HTML; la vista impostazioni legge e scrive le Soglie e le altre impostazioni tramite il Settings_Store; l'Icon_Set completo (20 icone del Requirement 34) è usato nelle viste di DiscCheck tramite le chiavi semantiche, con il DiscCheck_Icon_Pack e il DAS_Icon_Set in DAS_Design; l'icona dell'applicazione (Requirement 32) è impostata sull'eseguibile e sulla finestra; tutti i test del Requirement 37 sono superati.
9. WHEN viene dichiarata completata la milestone M8, THE DiscCheck SHALL soddisfare tutte le condizioni seguenti: la vista Archivio/Degrado mostra il trend nel tempo calcolato dal Degradation_Monitor; i promemoria di ricontrollo (`recheck_due`) vengono generati e mostrati; il C2_Reliability_Test è eseguibile su un Drive e ne riporta l'esito.
10. IF una condizione di una milestone Mn non è soddisfatta, THEN THE DiscCheck SHALL considerare la milestone Mn non completata e non considerare completata alcuna milestone Mk con k > n.
11. THE DiscCheck SHALL considerare completata una milestone solo se tutti i test automatici di `DiscCheck.Core.Tests`, `DiscCheck.Scsi.Tests` (con Fake_Transport) e `DAS.Design.Tests` relativi alle sue condizioni sono superati con 0 errori e i test di `DiscCheck.Hardware.Tests` relativi alle sue condizioni sono superati con il Drive di riferimento.

### Requirement 28: Design system condiviso DAS.Design

**User Story:** Come responsabile della famiglia di applicazioni DAS, voglio uno stile grafico in una libreria separata e indipendente da DiscCheck, così che ogni futura applicazione DAS lo riusi con una sola riga di codice.

#### Acceptance Criteria

1. THE DAS_Design SHALL essere un progetto Avalonia 11 su .NET 11 (`net11.0`) in `src/DAS.Design/`, incluso in `DiscCheck.sln`, con namespace radice `DAS.Design`, progettato per essere estratto in un repository autonomo e pubblicato come pacchetto NuGet con id `DAS.Design` senza modifiche al suo codice.
2. THE DAS_Design SHALL contenere: Design_Token (colori chiari e scuri, tipografia, spaziatura, raggi), dizionari di risorse e stili Avalonia (tema, pulsanti, tabelle, card, badge di stato OK/WARN/KO, palette dei grafici), Icon_Set (SVG) e DAS_Logo (SVG e `.ico`).
3. THE DAS_Design SHALL avere 0 riferimenti di progetto, di pacchetto o di assembly a `DiscCheck.*` e SHALL avere come unica dipendenza di terze parti Avalonia 11 (e il suo tema Fluent), senza dipendere da ScottPlot, Dapper, Serilog o altri pacchetti.
4. THE DAS_Design SHALL esporre il Theme_Entry_Point come tipo `DasTheme` (derivato da `Avalonia.Styling.Styles`) aggiungibile con una sola riga XAML `<das:DasTheme />` nell'elemento `Application.Styles`, e come metodo di estensione `AppBuilder.UseDasTheme()` equivalente, in modo che una nuova applicazione ottenga tutti i Design_Token, gli stili e le icone senza altre registrazioni.
5. WHEN `DasTheme` viene caricato, THE DAS_Design SHALL includere il tema Fluent di Avalonia e applicare la variante chiara o scura in base al `RequestedThemeVariant` dell'applicazione (Light, Dark o Default seguendo il tema di sistema).
6. WHERE un'applicazione definisce un Accent_Override, THE DAS_Design SHALL usare i valori ridefiniti per i soli Design_Token accent (`Das.Color.Accent`, `Das.Color.Accent.Dark`, `Das.Color.OnAccent`) in tutti gli stili, senza richiedere modifiche ai file della libreria.
7. THE DAS_Design SHALL usare il versionamento semantico 2.0.0 con versione iniziale 1.0.0 e mantenere nella radice del progetto un file `CHANGELOG.md` in cui ogni versione riporta data (AAAA-MM-GG) e l'elenco delle modifiche suddiviso in Added, Changed, Deprecated, Removed, Fixed.
8. WHEN una Token_Key, una chiave di icona o un file del DAS_Logo viene rimosso o rinominato, THE DAS_Design SHALL incrementare la versione major; WHEN viene aggiunto un elemento, THE DAS_Design SHALL incrementare almeno la versione minor; WHEN viene corretto un valore senza cambiare chiavi, THE DAS_Design SHALL incrementare almeno la versione patch.
9. WHEN una Token_Key o una chiave di icona viene dichiarata obsoleta, THE DAS_Design SHALL mantenerla funzionante per almeno una versione major successiva e SHALL riportare la sostituzione nella sezione Deprecated di `CHANGELOG.md`.
10. THE DAS_Design SHALL esporre nella proprietà `DasDesignInfo.Version` la versione semantica del pacchetto, uguale alla versione dell'ultima voce di `CHANGELOG.md`.
11. IF una Token_Key referenziata da un'applicazione non esiste in DAS_Design, THEN THE DAS_Design SHALL non sollevare eccezioni all'avvio dell'applicazione ospite (risoluzione con valore nullo di Avalonia) e il test del Requirement 37 criterio 1 SHALL fallire indicando file, riga e chiave mancante.

### Requirement 29: Design token e identità cromatica e tipografica

**User Story:** Come designer, voglio una palette e una tipografia ufficiali con valori esatti, così che tutte le applicazioni DAS abbiano lo stesso aspetto e testi leggibili.

#### Acceptance Criteria

1. THE DAS_Design SHALL definire come Design_Token `Das.Color.*` (valori `Color`) e `Das.Brush.*` (valori `SolidColorBrush`, con nome semantico) nelle varianti Light e Dark, e le viste SHALL usare solo i Das.Brush e gli stili, non i Das.Color diretti.
2. THE DAS_Design SHALL definire i colori di marca Light: Primary `#1F4FBF` ("DAS Blue"), Accent `#0B7A75` ("DAS Teal"), OnPrimary `#FFFFFF`, OnAccent `#FFFFFF`.
3. THE DAS_Design SHALL definire i colori di marca Dark: Primary `#7FA6FF`, Accent `#4FD1C5`, OnPrimary `#0F1420`, OnAccent `#0F1420`.
4. THE DAS_Design SHALL definire la scala neutra con 11 passi: N0 `#FFFFFF`, N50 `#F5F7FA`, N100 `#E8ECF2`, N200 `#D3DAE4`, N300 `#B4BECD`, N400 `#8D99AB`, N500 `#667388`, N600 `#4A5568`, N700 `#343E50`, N800 `#1F2735`, N900 `#0F1420`.
5. THE DAS_Design SHALL mappare i token di superficie e testo come segue. Light: Background N0, Surface N50, SurfaceAlt N100, Border N200, Text N900, TextSecondary N600. Dark: Background `#0F1420`, Surface `#1A2132`, SurfaceAlt N800, Border N700, Text `#E8ECF4`, TextSecondary N300.
6. THE DAS_Design SHALL definire i colori semantici di stato Light: OK `#146C2E` su sfondo `#E6F4EA`, WARN `#8A5A00` su sfondo `#FFF4DB`, KO `#B3261E` su sfondo `#FDE8E6`; e Dark: OK `#6FD98F` su sfondo `#12301D`, WARN `#F5BE4F` su sfondo `#3A2C0A`, KO `#FF8F86` su sfondo `#3F1512`, con token `Das.Color.Status.Ok`, `.OkSurface`, `.Warn`, `.WarnSurface`, `.Ko`, `.KoSurface`.
7. THE DAS_Design SHALL definire i colori dei grafici Light: C1 blu `#2F6FDE`, C2 rosso `#D63A2F`, CU viola `#7B3FC4`, velocità grigio `#6B7280`; e Dark: C1 `#6EA2FF`, C2 `#FF7A6E`, CU `#B88CFF`, velocità `#A0A8B8`, con token `Das.Color.Chart.C1`, `.C2`, `.CU`, `.Speed`, coerenti con i colori del Requirement 18 (C1 blu, C2 rosso, CU viola, velocità grigio).
8. THE DAS_Design SHALL garantire un Contrast_Ratio di almeno 4,5:1 per ogni coppia testo/sfondo seguente, in entrambe le varianti: Text su Background, Surface e SurfaceAlt; TextSecondary su Background e Surface; OnPrimary su Primary; OnAccent su Accent; Primary e Accent come testo su Background e Surface; Ok, Warn e Ko su rispettivi OkSurface, WarnSurface e KoSurface.
9. THE DAS_Design SHALL garantire un Contrast_Ratio di almeno 3:1 tra ciascun colore dei grafici (C1, C2, CU, velocità) e Background e Surface della stessa variante (criterio WCAG 2.1 1.4.11 per oggetti grafici).
10. THE DAS_Design SHALL escludere N400 e N500 dall'uso come colore di testo, riservandoli a bordi, separatori e icone disabilitate.
11. THE DAS_Design SHALL definire la tipografia con famiglia primaria Inter (licenza SIL OFL 1.1, file incorporati nella libreria, pesi 400, 600 e 700), con fallback ordinato "Segoe UI Variable Text", "Segoe UI", sans-serif, e famiglia monospaziata "Cascadia Mono" con fallback Consolas, monospace per CDB, hash e valori esadecimali.
12. THE DAS_Design SHALL definire i token di dimensione testo (dimensione/interlinea in px): Caption 12/16, Body 14/20, BodyLarge 16/24, Title 20/28, Headline 24/32, Display 32/40.
13. THE DAS_Design SHALL definire la scala di spaziatura `Das.Space.*` in px con i valori 4, 8, 12, 16, 24, 32, 48 (nomi 1, 2, 3, 4, 6, 8, 12 secondo il multiplo di 4 px) e la scala dei raggi `Das.Radius.*`: Small 4, Medium 8, Large 12, Pill 999.
14. IF il font Inter non è disponibile a runtime, THEN THE DAS_Design SHALL usare il fallback del criterio 11 senza errori e senza modificare le dimensioni dei Design_Token.
15. THE DAS_Design SHALL fornire ogni Design_Token nelle varianti Light e Dark con lo stesso insieme di chiavi, in modo che l'insieme delle chiavi Light sia uguale all'insieme delle chiavi Dark.

### Requirement 30: Stili e componenti condivisi

**User Story:** Come sviluppatore di un'applicazione DAS, voglio stili pronti per i controlli ricorrenti, così che non debba ridefinire l'aspetto di pulsanti, tabelle e stati.

#### Acceptance Criteria

1. THE DAS_Design SHALL fornire gli stili per `Button` nelle classi `primary`, `secondary` e `danger`, `DataGrid`/tabella (intestazione su SurfaceAlt, righe alternate su Surface e Background, bordo Border), `Border` nella classe `card` (sfondo Surface, bordo Border 1 px, raggio `Das.Radius.Medium`, padding `Das.Space.4`) e `TextBlock` nelle classi Caption, Body, BodyLarge, Title, Headline e Display.
2. THE DAS_Design SHALL fornire un controllo `DasStatusBadge` con proprietà `Status` (valori Ok, Warn, Ko) che mostra l'icona di stato (`das.status.ok`, `das.status.warning`, `das.status.ko`) e il testo "OK", "ATTENZIONE" o "KO" con i colori di stato e di sfondo del Requirement 29 criterio 6.
3. THE DasStatusBadge SHALL indicare lo stato con icona e testo oltre che con il colore, così che lo stato sia distinguibile senza percepire i colori.
4. WHILE un controllo interattivo ha il focus da tastiera, THE DAS_Design SHALL mostrare un contorno di 2 px con colore Primary e distanza di 2 px dal controllo.
5. WHILE un controllo è disabilitato, THE DAS_Design SHALL usare un'opacità del 40% sul controllo e mantenere inalterato il layout.
6. THE DAS_Design SHALL fornire un controllo `DasIcon` con proprietà `Name` (chiave semantica dell'icona, ad esempio `das.drive.optical`) e `Size` (predefinita 24 px; valori ammessi 16, 20, 24, 32), il cui colore segue la proprietà `Foreground` ereditata.
7. IF `DasIcon.Name` contiene una chiave inesistente, THEN THE DasIcon SHALL mostrare l'icona `das.status.warning` e scrivere un avviso nel Log dell'applicazione ospite, senza sollevare eccezioni.
8. THE DAS_Design SHALL fornire una palette ordinata di 8 colori per i confronti multi-curva (Requirement 19), `Das.Color.Series.1` … `Das.Color.Series.8`, distinguibili tra loro, con Contrast_Ratio di almeno 3:1 su Background e Surface in entrambe le varianti.
9. THE DAS_Design SHALL non dipendere da ScottPlot e SHALL esporre i colori dei grafici solo come Design_Token, così che l'applicazione ospite li converta nel proprio componente di grafico.

### Requirement 31: Logo DAS

**User Story:** Come responsabile del marchio, voglio un logo DAS concreto e identico in ogni applicazione, così che la famiglia di prodotti sia riconoscibile.

#### Acceptance Criteria

1. THE DAS_Logo SHALL essere composto da un monogramma e da un lockup orizzontale. Il monogramma è un disco ottico stilizzato su viewBox `0 0 64 64`: anello esterno di raggio 30 (centro 32,32) con spessore 8, foro centrale (hub) di diametro 16, e un settore di scansione di 90° (da ore 12 a ore 3) sull'anello, di colore Accent, con il resto dell'anello di colore Primary.
2. THE DAS_Logo SHALL avere come lockup orizzontale, su viewBox `0 0 200 64`, il monogramma a sinistra e a destra la scritta "DAS" in lettere maiuscole sans-serif geometriche di peso extra-bold, di altezza maiuscole 40 unità, distanza dal monogramma 16 unità e centrata verticalmente; la scritta SHALL essere convertita in tracciati vettoriali (path) senza dipendere da alcun font installato.
3. THE DAS_Logo SHALL essere fornito come file SVG in `src/DAS.Design/Assets/Logo/` nelle 4 varianti per ciascuna delle 2 forme (lockup `das-logo-*.svg`, monogramma `das-mark-*.svg`), cioè 8 file: `-color` (colori ufficiali su piastra bianca arrotondata con raggio 12 unità, utilizzabile su qualunque sfondo), `-light` (sfondo trasparente per sfondi chiari: anello Primary Light `#1F4FBF`, settore Accent Light `#0B7A75`, scritta `#0F1420`), `-dark` (sfondo trasparente per sfondi scuri: anello `#7FA6FF`, settore `#4FD1C5`, scritta `#E8ECF4`) e `-mono` (un solo colore tramite `currentColor`, con il settore di scansione reso da un'incisione trasparente di 2 unità).
4. THE DAS_Logo SHALL essere fornito come icona applicazione `Assets/Icons/das.ico` con le 7 dimensioni 16, 24, 32, 48, 64, 128 e 256 px contenenti il monogramma, con colori della variante `-color`.
5. THE DAS_Logo SHALL avere il Contrast_Ratio di almeno 3:1 tra le parti colorate della variante `-light` e `#FFFFFF` e `#F5F7FA`, e tra le parti colorate della variante `-dark` e `#0F1420` e `#1A2132`.
6. THE DAS_Design SHALL fornire il file `Assets/Logo/logo.sha256` con l'hash SHA-256 di ciascun file SVG e `.ico` del DAS_Logo, aggiornato solo con incremento della versione di DAS_Design.
7. THE DAS_Design SHALL esporre il DAS_Logo come risorse Avalonia con chiavi `Das.Logo.Lockup`, `Das.Logo.Mark` (varianti scelte automaticamente in base alla variante di tema Light o Dark) e come controllo `DasLogo` con proprietà `Form` (Lockup o Mark) e `Height`.
8. THE DAS_Logo SHALL essere identico in ogni applicazione DAS: le applicazioni SHALL referenziare il DAS_Logo solo da DAS_Design, senza copiare, ricolorare, ritagliare o ricreare i file.

### Requirement 32: Regole d'uso del logo e icona dell'applicazione

**User Story:** Come responsabile del marchio, voglio regole di utilizzo verificabili del logo, così che resti sempre invariato e leggibile.

#### Acceptance Criteria

1. THE DAS_Logo SHALL non essere modificato da alcuna applicazione: sono vietati ridimensionamento non proporzionale, rotazione, ricolorazione con colori diversi da quelli delle varianti del Requirement 31, effetti (ombre, sfumature, contorni), sostituzione del font della scritta e riposizionamento degli elementi.
2. WHERE il DAS_Logo è mostrato in un'applicazione, THE App SHALL mostrare il nome dell'applicazione ("DiscCheck" o il nome definitivo di D3) accanto al DAS_Logo, a destra del lockup, separato da una linea verticale di 1 px con colore Border e da una distanza di 16 px per lato, con il nome in Inter peso 600 di dimensione Title (20/28).
3. THE DAS_Logo SHALL avere una dimensione minima, per il lockup, di 96 px di larghezza (25 mm in stampa) e, per il monogramma, di 16 px di larghezza (5 mm in stampa); IF lo spazio disponibile è inferiore alla dimensione minima del lockup, THEN THE App SHALL mostrare il monogramma.
4. THE DAS_Logo SHALL avere intorno uno spazio libero (clear space) di almeno x, con x pari al diametro dell'hub, cioè 25% dell'altezza del monogramma, su tutti e 4 i lati, privo di testo, bordi e altri elementi grafici.
5. WHEN l'App mostra la finestra principale, THE App SHALL mostrare il lockup del DAS_Logo di altezza 32 px nell'intestazione, in alto a sinistra, nella variante `-light` o `-dark` corrispondente al tema attivo, con il nome dell'applicazione secondo il criterio 2.
6. WHEN l'utente apre la finestra "Informazioni" (About), THE App SHALL mostrare il lockup del DAS_Logo di altezza 64 px, il nome dell'applicazione, la versione dell'applicazione e la versione di DAS_Design (`DasDesignInfo.Version`).
7. WHEN l'App viene avviata, THE App SHALL mostrare una schermata iniziale (splash) con il lockup del DAS_Logo di altezza 128 px sulla variante `-color`, chiuderla alla visualizzazione della finestra principale e mantenerla visibile per non più di 5 secondi.
8. THE App SHALL usare come icona dell'applicazione (eseguibile, barra del titolo e barra delle applicazioni) il file `disccheck.ico`, composto dal monogramma del DAS_Logo (invariato) con un badge dell'applicazione in basso a destra, e il DAS_Logo stesso SHALL restare invariato.
9. THE `disccheck.ico` SHALL contenere le 7 dimensioni 16, 24, 32, 48, 64, 128 e 256 px; il badge SHALL essere un cerchio di colore Accent Light `#0B7A75` con la spunta bianca `das.status.ok` e un diametro pari al 40% del lato dell'icona (50% per 16 e 24 px, con la sola spunta senza anello).
10. THE DiscCheck SHALL generare `disccheck.ico` dai sorgenti SVG del DAS_Logo e del DiscCheck_Icon_Pack tramite uno script riproducibile in `src/DiscCheck.App/Assets/`, senza copiare i file del DAS_Logo.
11. WHILE una finestra è visualizzata su sfondo con Contrast_Ratio non sufficiente per la variante in uso (sotto 3:1), THE App SHALL usare la variante `-color`.

### Requirement 33: Regole di stile delle icone

**User Story:** Come designer, voglio regole di stile precise per le icone, così che tutte le icone DAS siano coerenti, scalabili e seguano il tema.

#### Acceptance Criteria

1. THE Icon_Set SHALL fornire ogni icona come file SVG con `viewBox="0 0 24 24"`, senza attributi `width` e `height` fissi, in `src/DAS.Design/Assets/Icons/` (DAS_Icon_Set) e `src/DiscCheck.App/Assets/Icons/` (DiscCheck_Icon_Pack).
2. THE Icon_Set SHALL disegnare le icone con tratto `stroke-width="1.5"`, `stroke-linecap="round"`, `stroke-linejoin="round"`, `fill="none"` (salvo punti pieni di diametro non superiore a 2 unità), con area utile di 20 × 20 unità (margine di 2 unità) e angoli arrotondati con raggio di 2 unità per i rettangoli.
3. THE Icon_Set SHALL usare esclusivamente `currentColor` per `stroke` e `fill` colorati, senza valori colore esadecimali, `rgb()`, nomi di colore, gradienti, filtri, immagini raster o testo, così che il colore segua la proprietà `Foreground`.
4. THE Icon_Set SHALL usare nomi file nella forma `<chiave>.svg` con chiave in minuscolo e punti come separatori (ad esempio `das.drive.optical.svg`).
5. THE Icon_Set SHALL esporre ogni icona come risorsa Avalonia con chiave `Das.Icon.<chiave>` per il DAS_Icon_Set e `DiscCheck.Icon.<chiave>` per il DiscCheck_Icon_Pack, oltre al nome semantico usato da `DasIcon.Name`.
6. THE Icon_Set SHALL mantenere leggibili le icone alle dimensioni di 16, 20, 24 e 32 px, con spessore del tratto scalato proporzionalmente.
7. WHEN un'icona rappresenta uno stato (OK, WARN, KO), THE Icon_Set SHALL distinguere gli stati con forme diverse (spunta, triangolo con punto esclamativo, croce) e non con il solo colore.

### Requirement 34: Insieme delle icone

**User Story:** Come sviluppatore, voglio un elenco stabile di icone a tema disco ottico con chiavi semantiche, così che le applicazioni DAS riusino quelle generiche.

#### Acceptance Criteria

1. THE DAS_Icon_Set SHALL contenere le 15 icone generiche, con chiavi stabili e indipendenti dall'applicazione:
   1. `das.drive.optical` (unità ottica / drive);
   2. `das.disc.cd` (disco CD);
   3. `das.history` (storico, orologio con freccia antioraria);
   4. `das.settings` (impostazioni, ingranaggio);
   5. `das.export.csv` (export CSV, foglio con tabella e freccia in uscita);
   6. `das.export.png` (export PNG, foglio con immagine e freccia in uscita);
   7. `das.export.html` (export HTML, foglio con parentesi angolari e freccia in uscita);
   8. `das.eject` (espulsione, triangolo sopra barra);
   9. `das.reset` (reset drive, freccia circolare);
   10. `das.status.warning` (attenzione, triangolo con punto esclamativo);
   11. `das.status.ok` (OK, spunta in cerchio);
   12. `das.status.ko` (KO, croce in cerchio);
   13. `das.reminder.bell` (promemoria, campanella);
   14. `das.theme.light` (tema chiaro, sole);
   15. `das.theme.dark` (tema scuro, luna).
2. THE DiscCheck_Icon_Pack SHALL contenere le 5 icone specifiche di DiscCheck:
   1. `disccheck.scan.pulse` (scansione qualità: disco con onda/impulso sul settore di scansione);
   2. `disccheck.chart.errors` (grafico errori C1/C2/CU: assi con tre barre di altezze diverse);
   3. `disccheck.verify.bitperfect` (verifica bit-perfect: spunta sul disco);
   4. `disccheck.compare.curves` (confronto: due curve sovrapposte);
   5. `disccheck.archive.degradation` (archivio/degrado: orologio e disco, con la lancetta che indica il passare del tempo sul disco).
3. THE Icon_Set SHALL contenere in totale 20 icone, con ogni chiave del DAS_Icon_Set con prefisso `das.` e ogni chiave del DiscCheck_Icon_Pack con prefisso `disccheck.`, senza chiavi duplicate.
4. THE Icon_Set SHALL non modificare né riutilizzare per un significato diverso una chiave già pubblicata; IF il significato cambia, THEN THE Icon_Set SHALL aggiungere una nuova chiave e dichiarare obsoleta la precedente (Requirement 28 criterio 9).
5. THE DiscCheck SHALL usare le icone nelle viste seguendo questo abbinamento minimo: vista Drive `das.drive.optical` e `das.reset`; vista Disco `das.disc.cd` e `das.eject`; vista Scansione `disccheck.scan.pulse` e `disccheck.chart.errors`; vista Verifica `disccheck.verify.bitperfect`; vista Confronto `disccheck.compare.curves`; vista Storico `das.history`; vista Archivio/Degrado `disccheck.archive.degradation` e `das.reminder.bell`; vista Impostazioni `das.settings`, `das.theme.light` e `das.theme.dark`; azioni di export `das.export.csv`, `das.export.png`, `das.export.html`; Verdetti e avvisi `das.status.ok`, `das.status.warning`, `das.status.ko`.
6. WHERE una nuova applicazione DAS è sviluppata, THE DAS_Design SHALL rendere disponibili le 15 icone del DAS_Icon_Set senza richiedere il DiscCheck_Icon_Pack.
7. THE DAS_Design SHALL non contenere alcuna icona con prefisso `disccheck.` nel proprio pacchetto.

### Requirement 35: Uso del design system in DiscCheck

**User Story:** Come sviluppatore, voglio che DiscCheck usi lo stile solo tramite DAS.Design, così che un cambio di tema o di marca non richieda modifiche alle viste.

#### Acceptance Criteria

1. THE DiscCheck SHALL referenziare `DAS.Design` come unico fornitore di stili, colori, icone e logo, aggiungendo il Theme_Entry_Point `DasTheme` in `App.axaml`.
2. THE DiscCheck SHALL non contenere nei file `.axaml` di `DiscCheck.App` colori letterali (valori `#RRGGBB`, `#AARRGGBB`, nomi di colore o `Color="..."`), `SolidColorBrush` letterali, font letterali (`FontFamily` diversi da Token_Key) né dimensioni di carattere, spaziature e raggi letterali al posto dei Design_Token, ad eccezione dei valori di layout non coperti da token (larghezze, altezze, griglie).
3. THE DiscCheck SHALL riferire i Design_Token e gli stili con `DynamicResource` (per i valori che cambiano con il tema) o `StaticResource` (per i valori invarianti), usando solo Token_Key esistenti in DAS_Design.
4. THE DiscCheck SHALL ottenere i colori dei grafici del Requirement 18 e 19 esclusivamente dai Design_Token `Das.Color.Chart.*` e `Das.Color.Series.*`, senza costanti di colore nel codice C#.
5. WHERE DiscCheck richiede un accento specifico, THE DiscCheck SHALL definirlo con un Accent_Override nel proprio `App.axaml`, senza modificare DAS_Design.
6. THE DiscCheck SHALL mostrare i Verdetti OK, WARN e KO tramite `DasStatusBadge`.
7. IF DiscCheck usa nelle viste un'icona del DiscCheck_Icon_Pack, THEN THE DiscCheck SHALL referenziarla tramite la chiave `disccheck.*` e non SHALL spostarla in DAS_Design.

### Requirement 36: Licenza degli asset grafici e decisione D5

**User Story:** Come responsabile del progetto, voglio una licenza esplicita e tracciata per logo, icone e font, così che le applicazioni DAS possano ereditarli senza ambiguità.

#### Acceptance Criteria

1. THE DAS_Design SHALL dichiarare che logo, icone e Design_Token sono opere originali create per il progetto, e SHALL contenere nella radice del progetto un file `LICENSE-ASSETS.md` con titolare, anno, licenza scelta e testo della licenza.
2. THE decisione D5 SHALL avere le opzioni: (a) stessa licenza del progetto per codice e asset; (b) licenza del progetto per il codice e CC-BY 4.0 per gli asset grafici (logo, icone); (c) CC0 1.0 per gli asset grafici.
3. WHILE la decisione D5 è nello stato "aperta", THE DAS_Design SHALL riportare in `LICENSE-ASSETS.md` la dicitura "licenza in definizione (D5)" e SHALL non essere pubblicato come pacchetto NuGet né come repository pubblico autonomo.
4. WHEN viene preparato il primo rilascio di DiscCheck oppure la prima pubblicazione di DAS_Design (la prima delle due), THE DiscCheck SHALL avere la decisione D5 nello stato "risolta", con opzione scelta, data (AAAA-MM-GG) e motivazione di almeno una frase.
5. THE DAS_Design SHALL includere il font Inter con il testo della licenza SIL OFL 1.1 in `Assets/Fonts/OFL.txt`, mantenendo la licenza del font indipendente da D5.
6. WHERE D5 è risolta con l'opzione (b), THE DAS_Design SHALL riportare in `LICENSE-ASSETS.md` e nell'About di ogni applicazione che mostra il DAS_Logo l'indicazione di attribuzione richiesta da CC-BY 4.0.
7. THE DAS_Design SHALL non includere asset di terze parti (icone, immagini, font) diversi da Inter, con la relativa licenza dichiarata.

### Requirement 37: Test del design system

**User Story:** Come sviluppatore, voglio test automatici che garantiscano coerenza di token, icone, contrasto e indipendenza, così che il design system resti riusabile.

#### Acceptance Criteria

1. THE `DiscCheck.Core.Tests` SHALL includere un test che analizza tutti i file `.axaml` di `DiscCheck.App` ed estrae ogni Token_Key referenziata con `DynamicResource` o `StaticResource` che inizia per `Das.` o `DiscCheck.`, e che fallisce se almeno una chiave non esiste in DAS_Design (o nel proprio Accent_Override per le chiavi dichiarate), indicando file, riga e chiave.
2. THE `DiscCheck.Core.Tests` SHALL includere un test che fallisce se un file `.axaml` di `DiscCheck.App` contiene un colore letterale, un `SolidColorBrush` letterale o un `FontFamily` letterale (Requirement 35 criterio 2), indicando file e riga.
3. THE `DAS.Design.Tests` SHALL includere un test che verifica per ogni file SVG dell'Icon_Set: XML valido, `viewBox="0 0 24 24"`, assenza di `width` e `height`, uso di `currentColor` per ogni `stroke` e `fill` colorato, assenza di valori colore letterali, gradienti, filtri e testo, `stroke-width="1.5"` e nome file uguale alla chiave nella forma del Requirement 33 criterio 4.
4. THE `DAS.Design.Tests` SHALL includere un test che verifica che l'Icon_Set contenga esattamente le 20 chiavi del Requirement 34, che nessuna chiave sia duplicata, che le chiavi `das.*` siano nel pacchetto DAS_Design e che nessuna chiave `disccheck.*` vi sia presente.
5. THE `DAS.Design.Tests` SHALL includere un test che calcola il Contrast_Ratio per ogni coppia del Requirement 29 criteri 8 e 9 (e del Requirement 30 criterio 8) in entrambe le varianti Light e Dark, leggendo i valori dai Design_Token, e fallisce se una coppia di testo è sotto 4,5:1 o una coppia grafica sotto 3:1, indicando la coppia e il valore calcolato.
6. THE `DAS.Design.Tests` SHALL includere un test che verifica che le chiavi dei Design_Token Light siano uguali a quelle Dark (Requirement 29 criterio 15).
7. THE `DAS.Design.Tests` SHALL includere un test che verifica che il progetto `DAS.Design` compili e che il relativo `.csproj` e l'assembly compilato non abbiano alcun riferimento a progetti o assembly con nome che inizia per `DiscCheck`.
8. THE `DAS.Design.Tests` SHALL includere un test che verifica che `DasTheme` sia caricabile in un'applicazione Avalonia di test (headless) che referenzia solo `DAS.Design`, e che `DasIcon` e `DasStatusBadge` siano istanziabili in entrambe le varianti di tema.
9. THE `DAS.Design.Tests` SHALL includere un test che verifica che gli hash SHA-256 dei file del DAS_Logo coincidano con `Assets/Logo/logo.sha256`, che i file `das.ico` e `disccheck.ico` contengano esattamente le 7 dimensioni 16, 24, 32, 48, 64, 128 e 256 px, e che la solution di DiscCheck non contenga copie dei file del DAS_Logo fuori da `src/DAS.Design/`.
10. THE `DAS.Design.Tests` SHALL includere un test che verifica che `DasDesignInfo.Version` sia uguale alla versione dell'ultima voce di `CHANGELOG.md` e che `CHANGELOG.md` contenga la voce 1.0.0.
11. WHEN viene eseguito `dotnet test --filter "Category!=Hardware"`, THE DiscCheck SHALL eseguire i test di `DAS.Design.Tests` senza Drive ottici né connessione di rete, con 0 test falliti e 0 test saltati.
12. THE `DiscCheck.Core.Tests` SHALL includere un test che analizza i file `.axaml` e `.cs` di `DiscCheck.App` e fallisce, indicando file e riga, se una vista istanzia direttamente `AvaPlot` o `ScottPlot.Plot` invece di usare il Chart_View, oppure se una delle viste Scansione, Storico, Confronto e Archivio/Degrado non contiene un Chart_View (Requirement 38 criterio 15).
13. THE `DAS.Design.Tests` SHALL includere test headless che verificano: (a) `ChartThemeApplier.Apply` imposta in entrambe le varianti i colori di sfondo, griglia, assi, tick e legenda uguali ai Design_Token del Requirement 38 criterio 2; (b) il formattatore dei tick tempo produce `mm:ss` e il parsing del testo prodotto restituisce il valore originale (round-trip); (c) per ogni tempo del puntatore, il campione scelto dal Chart_Cursor minimizza la distanza in tempo; (d) per ogni Chart_Selection, minimo ≤ media ≤ massimo e il numero di campioni considerati è uguale a quello dei campioni non nulli nell'intervallo; (e) l'export PNG ha dimensioni pari a 2 volte quelle logiche e l'export SVG è XML valido; (f) le serie hanno stili di linea e marcatori distinti secondo il Requirement 38 criterio 12.
14. THE `DAS.Design.Tests` SHALL includere un test di prestazione, marcato `[Trait("Category","Performance")]` ed eseguito con `dotnet test --filter "Category!=Hardware"`, che verifica gli obiettivi del Requirement 38 criterio 14 su dati sintetici, e un test che verifica che `DAS.Design.Charts` non abbia riferimenti a progetti o assembly con nome che inizia per `DiscCheck`.

### Requirement 38: Qualità professionale dei grafici

**User Story:** Come utente e come sviluppatore di applicazioni DAS, voglio grafici dall'aspetto professionale, coerenti e leggibili in tema chiaro e scuro, ottenuti da un unico strato di stile condiviso, così che ogni vista e ogni futura applicazione DAS mostrino dati di qualità senza duplicare codice.

#### Acceptance Criteria

1. THE DiscCheck SHALL usare ScottPlot 5 (ScottPlot.Avalonia 5.x) come unica libreria di grafici, scelta perché distribuita con licenza MIT, ottimizzata per grandi insiemi di dati di segnale (decine di migliaia di punti con ridisegno interattivo) e fornita come controllo Avalonia nativo, e SHALL non introdurre altre librerie di grafici.
2. THE Chart_Style SHALL applicare a ogni grafico, leggendoli dai Design_Token nella variante di tema attiva e senza colori letterali nel codice: sfondo della figura `Das.Color.Background`; sfondo dell'area dati `Das.Color.Surface`; griglia maggiore `Das.Color.Border` con spessore 1 px e opacità 100%; griglia minore `Das.Color.Border` con opacità 40%; assi e linee dei tick `Das.Color.Border`; testo di tick, etichette degli assi e legenda `Das.Color.Text` (tick) e `Das.Color.TextSecondary` (etichette degli assi); sfondo della legenda `Das.Color.SurfaceAlt` con bordo `Das.Color.Border`.
3. THE Chart_Style SHALL usare il font Inter (fallback del Requirement 29 criterio 11) con dimensione 12 px per tick e legenda (Caption), 14 px peso 600 per le etichette degli assi (Body) e 20 px peso 600 per l'eventuale titolo del grafico (Title); IF Inter non è disponibile, THEN THE Chart_Style SHALL usare il fallback senza errori e senza modificare le dimensioni.
4. WHEN l'utente cambia tema (Requirement 22 criterio 7) oppure il tema di sistema cambia con `RequestedThemeVariant` Default, THE Chart_Style SHALL riapplicarsi a tutti i Chart_View aperti entro 1 secondo, senza perdere dati, zoom, Chart_Selection e serie visibili.
5. THE Chart_Style SHALL etichettare gli assi con unità di misura: asse X della scansione "Tempo (mm:ss)", asse Y sinistro "Errori al secondo (errori/s)", asse Y destro "Velocità (x)"; nel grafico di Confronto l'asse Y riporta il nome della metrica scelta e l'unità "errori/s"; nel grafico di trend l'asse X "Data" e l'asse Y il nome della serie con unità.
6. THE Chart_Style SHALL formattare i tick dell'asse X del tempo come `mm:ss` (minuti anche oltre 59, esempio `80:00`) e scegliere l'intervallo tra i tick maggiori tra 1, 2, 5, 10, 15, 30, 60, 120, 300 e 600 secondi, in modo che il numero di tick maggiori visibili sia compreso tra 4 e 12 e che nessuna etichetta di tick si sovrapponga a un'altra; i tick dell'asse Y SHALL essere interi per le scansioni (errori/s) e l'estremo inferiore SHALL essere 0.
7. THE Chart_Style SHALL posizionare la legenda fuori dall'area dati (sotto il grafico, centrata), con una voce per serie visibile contenente stile di linea o marcatore e nome (Requirement 19 criterio 1 per il Confronto), in modo che la legenda non si sovrapponga ad alcun punto o barra dei dati a qualunque livello di zoom; IF le voci non entrano su una riga, THEN THE Chart_Style SHALL disporle su più righe, con un massimo di 4 righe.
8. IF un Chart_View non ha dati da mostrare, THEN THE Chart_View SHALL mostrare l'area dati con assi e griglia in stile (X da 0 a 60 s, Y da 0 a 10) e il messaggio "Nessun dato da visualizzare" in `Das.Color.TextSecondary` al centro, senza sollevare eccezioni.
9. THE Chart_View SHALL formattare i numeri mostrati nella UI (tick, tooltip, statistiche di selezione, riepilogo) con la cultura corrente dell'utente, con C1, C2, CU come interi, SpeedX con 1 decimale e suffisso "x", C1 medio con 2 decimali; i numeri scritti nei file esportati SHALL usare la cultura invariante (Requirement 21 criterio 12).
10. WHEN il fattore di scala del display è compreso tra 100% e 300%, THE Chart_View SHALL renderizzare il grafico alla risoluzione fisica del display (nessuna scalatura bitmap che sfoca testo e linee), con spessore delle linee di serie 1,5 unità indipendenti dal dispositivo, linee di griglia di 1 pixel fisico e testo non sfocato, e SHALL ridisegnare il grafico alla nuova risoluzione entro 1 secondo da un cambio di scala del display.
11. THE Chart_Style SHALL usare per ogni oggetto grafico dei dati (barre, linee, marcatori, linee soglia, evidenziazioni) colori con Contrast_Ratio di almeno 3:1 su `Das.Color.Surface` della variante attiva (Requirement 29 criteri 8 e 9), e per ogni testo del grafico un Contrast_Ratio di almeno 4,5:1 su `Das.Color.Background` e `Das.Color.Surface`.
12. THE Chart_Style SHALL rendere le serie distinguibili anche senza percepire i colori: nei grafici di scansione C1 con barre piene e marcatore cerchio nel tooltip e nella legenda, C2 con barre con riempimento a tratteggio diagonale e marcatore quadrato, CU con barre con riempimento a punti e marcatore rombo, SpeedX con linea tratteggiata; nei grafici con curve multiple (Requirement 19) le 8 curve usano in ordine gli stili di linea pieno, tratteggio, punteggiato e tratto-punto, ciclati, e i marcatori cerchio, quadrato, rombo, triangolo, in modo che due curve qualsiasi differiscano per colore e per almeno una tra stile di linea e forma del marcatore.
13. WHILE il Chart_View ha il focus da tastiera, THE Chart_View SHALL mostrare il contorno di focus del Requirement 30 criterio 4 e SHALL rendere disponibili da tastiera le operazioni di cursore (frecce sinistra e destra), reset zoom (tasto `0`), zoom avanti e indietro (tasti `+` e `-`) e cancellazione della selezione (tasto Esc).
14. THE Chart_View SHALL soddisfare i seguenti obiettivi di prestazione su un PC di riferimento che soddisfa i requisiti di Windows 10/11 x64: WHILE una scansione è in corso, aggiornamento live del grafico a 1 campione al secondo fino a 6.000 campioni con ridisegno non superiore a 33 ms al 95° percentile e con blocchi del thread UI non superiori a 100 ms; ridisegno completo di un grafico di Confronto con 8 curve da 6.000 campioni ciascuna non superiore a 100 ms; risposta a zoom, pan, cursore e selezione entro 100 ms; frequenza di ridisegno limitata a un massimo di 30 al secondo, accorpando gli aggiornamenti ravvicinati.
15. THE App SHALL mostrare i grafici delle viste Scansione, Storico, Confronto e Archivio/Degrado tramite lo stesso controllo Chart_View, senza che alcuna vista istanzi direttamente `AvaPlot` o `ScottPlot.Plot` né applichi colori, font o proprietà di stile propri.
16. THE DAS_Charts SHALL avere come uniche dipendenze `DAS.Design` e ScottPlot.Avalonia 5.x, 0 riferimenti a `DiscCheck.*`, namespace radice `DAS.Design.Charts`, versionamento semantico e `CHANGELOG.md` secondo il Requirement 28 criteri 7, 8 e 9, e SHALL essere riusabile da qualunque applicazione DAS con la sola registrazione di `DasTheme` (Requirement 28) e un riferimento al progetto o pacchetto `DAS.Design.Charts`.
17. IF un Design_Token richiesto dal Chart_Style non è risolvibile a runtime, THEN THE Chart_Style SHALL usare il valore di ripiego della variante Light, scrivere un avviso nel Log con la Token_Key mancante e proseguire senza sollevare eccezioni.

### Requirement 39: Interattività dei grafici

**User Story:** Come utente, voglio esplorare i grafici con cursore, zoom, selezione e soglie, così che possa individuare e quantificare i punti con errori.

#### Acceptance Criteria

1. WHEN il puntatore del mouse si muove sull'area dati di un Chart_View con almeno 1 campione, THE Chart_Cursor SHALL mostrare entro 100 ms una linea verticale agganciata al campione con il tempo più vicino alla posizione del puntatore, e un tooltip con tempo (`mm:ss`), C1 (errori/s), C2 (errori/s), CU (errori/s) e SpeedX (x) di quel campione, con "n/d" per ogni valore nullo.
2. THE Chart_Cursor SHALL mantenere il tooltip interamente dentro l'area del Chart_View (spostandolo a sinistra o a destra del puntatore in prossimità dei bordi) e SHALL nascondere linea e tooltip entro 100 ms quando il puntatore esce dall'area dati.
3. WHEN il Chart_View è nella vista Confronto, THE Chart_Cursor SHALL mostrare nel tooltip il tempo e il valore della metrica scelta per ciascuna curva visibile, con nome della curva (data, supporto, velocità) e "n/d" se la curva non ha un campione a quel tempo.
4. WHEN l'utente ruota la rotella del mouse sul Chart_View, THE Chart_View SHALL eseguire lo zoom sull'asse X centrato sulla posizione del puntatore con fattore 1,25 per scatto, con intervallo visibile minimo di 5 secondi e massimo pari alla durata completa dei dati; WHEN l'utente trascina con il pulsante sinistro premuto, THE Chart_View SHALL spostare la vista sull'asse X senza uscire dall'intervallo da 0 alla durata dei dati.
5. WHEN l'utente esegue doppio clic sull'area dati oppure preme il pulsante "Reset zoom" (icona `das.reset`, sempre visibile nell'angolo in alto a destra del Chart_View e abilitato solo se la vista non corrisponde all'auto-fit), THE Chart_View SHALL ripristinare entro 100 ms l'auto-fit: asse X da 0 alla durata dei dati e asse Y da 0 a 1,05 volte il valore massimo visibile (minimo 10 per le scansioni), senza modificare i dati.
6. WHEN l'utente trascina con il pulsante sinistro premuto e il tasto Maiusc premuto sull'area dati, THE Chart_View SHALL creare una Chart_Selection evidenziata con un riquadro semitrasparente (opacità 25%) e mostrare entro 100 ms un pannello con, per C1, C2, CU e SpeedX, minimo, massimo e media (2 decimali) e per C2 e CU anche la somma, calcolati sui soli campioni non nulli compresi nell'intervallo (estremi inclusi) e con la durata della selezione in `mm:ss`; IF l'intervallo non contiene alcun campione, THEN THE Chart_View SHALL mostrare "n/d" per tutti i valori.
7. WHEN l'utente preme il tasto Esc o fa clic fuori dalla Chart_Selection con il tasto Maiusc non premuto, THE Chart_View SHALL cancellare la Chart_Selection e nascondere il pannello entro 100 ms.
8. WHERE lo scanner della scansione è LiteOnScanner (C1 presente) e l'opzione "Mostra soglie" è attiva (predefinita attiva), THE Chart_View SHALL tracciare sull'asse Y sinistro due linee orizzontali tratteggiate di spessore 1,5 unità con etichetta testuale: alla soglia C1 massimo per WARN (predefinita 50/s, colore `Das.Color.Status.Warn`) e alla soglia C1 massimo per KO (predefinita 220/s, colore `Das.Color.Status.Ko`), con i valori letti dal Settings_Store (Requirement 11 criterio 5) e aggiornati entro 1 secondo da una modifica delle Soglie.
9. THE Chart_View SHALL non modificare l'intervallo dell'asse Y per includere le linee soglia, e SHALL disegnare ciascuna linea soglia solo se il suo valore rientra nell'intervallo Y visibile; WHERE lo scanner è StandardC2Scanner, THE Chart_View SHALL non mostrare le linee soglia.
10. WHEN uno Scan_Sample ha C2 maggiore di 0 oppure CU maggiore di 0, THE Chart_View SHALL evidenziare quel secondo con una banda verticale a tutta altezza di larghezza 1 secondo, colore `Das.Color.Status.KoSurface` con opacità 60%, e con un marcatore triangolare in `Das.Color.Status.Ko` nella parte alta dell'area dati, in modo che l'evidenziazione sia visibile anche senza percepire i colori e SHALL mantenerla a ogni livello di zoom.
11. THE Chart_View SHALL mostrare accanto al pulsante "Reset zoom" il numero di secondi evidenziati con C2 o CU maggiori di 0, con etichetta "Secondi con errori C2/CU: N", aggiornato entro 1 secondo da ogni nuovo campione.
12. WHEN il Chart_View riceve un nuovo Scan_Sample durante una scansione, WHILE la vista è in auto-fit, THE Chart_View SHALL estendere automaticamente l'asse X, e WHILE l'utente ha modificato zoom o spostamento, THE Chart_View SHALL mantenere la vista corrente senza spostarla.
13. FOR ALL tempi del puntatore t, il campione selezionato dal Chart_Cursor SHALL essere quello con minima distanza assoluta |t_i − t| e, a parità di distanza, quello con tempo minore.
