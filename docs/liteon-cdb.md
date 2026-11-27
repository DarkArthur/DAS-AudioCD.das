# CDB vendor Lite-On (estratti da QPxTool_Source)

Documento del task 13.2 (Requirement 8.1, 8.3, 8.4). Ogni byte riportato qui è stato letto nel sorgente. Nessun valore è dedotto o inventato. Dove un valore non è nei file indicati, il documento lo dice.

## Sorgente

- Repository: `github.com/speed47/qpxtool`, ramo `master`, commit `9c6a3fdb5e2759db1fd1b8d719e85d086b2b4489` (letto via raw.githubusercontent.com).
- File principali: `plugins/liteon/qscan_cmd.cpp`, `plugins/liteon/qscan_plugin.cpp`.
- File di supporto letti per risolvere lunghezza del CDB, direzione, flag e ciclo di lettura: `lib/qpxtransport/qpx_transport.cpp` (`Scsi_Command::operator[]`, `transport`), `lib/qpxtransport/qpx_mmc.cpp` (`seek`, `set_cd_speed`), `lib/qpxtransport/include/qpx_transport.h` (enum `Direction`), `lib/qpxscan/include/qpx_scan_plugin_api.h` (`set_read_speed`), `lib/qpxscan/qpx_scan_algo.cpp` (`run_cd_errc`), `console/qscan/qscan.cpp` (opzione `LITEON_FORCE_OLD`).
- Licenza: GPL-2.0 or later (intestazione dei file). Vedi D2 in `docs/decisions.md` (distribuzione con GPL-2.0, opzione "a").

## Regole di costruzione del CDB in QPxTool

- `dev->cmd[0] = ...` azzera l'intero CDB (e il sense) prima di scrivere il byte 0. Ogni comando parte quindi da un CDB a zero: i byte non citati sono `0x00`.
- La lunghezza del CDB è `indice più alto scritto + 1`, ma almeno `COMMAND_SIZE(opcode)`, con la tabella `{6, 10, 10, 12, 16, 12, 10, 10}` indicizzata da `opcode >> 5`. Gli opcode `0xDF` e `0xF3` (gruppo 6 e 7) danno un minimo di 10 byte. Dove il sorgente scrive `cmd[11] = 0x00`, il CDB è di **12 byte**. Dove non lo scrive, è di **10 byte**.
- `READ` = dati dal drive all'host (SCSI data-in). `WRITE` = dati dall'host al drive (data-out). `NONE` = nessun trasferimento dati. Valori dell'enum: `NONE`=3, `READ`=2, `WRITE`=1 (da `CGC_DATA_*`).
- `Scsi_Command::transport` non fa retry. Un errore (sense o fallimento ioctl) è restituito come codice diverso da 0.

## Comandi per i CD (CD ERRC)

Notazione: i byte non elencati sono 0. `LEN` = lunghezza del CDB. `DATA` = direzione e lunghezza del buffer dati.

### SET CD SPEED (passo precedente all'init, contesto)

Funzione `set_cd_speed` in `lib/qpxtransport/qpx_mmc.cpp`, chiamata da `set_rw_speeds` tramite `scan_plugin::set_read_speed` (`qpx_scan_plugin_api.h`).

| Byte | Valore |
|------|--------|
| 0 | `0xBB` (`MMC_SET_SPEED`) |
| 1 | `0x01` |
| 2-3 | velocità di lettura in kB/s, big-endian (`speed × speed_mult`, con `speed_mult` = 176 per CD; `0xFFFF` se 0) |
| 4-5 | velocità di scrittura in kB/s, big-endian (`0xFFFF` se non impostata) |
| 11 | `0x00` |

LEN = 12, DATA = NONE, 0 byte. Nota: `set_rw_speeds` usa prima SET STREAMING se il drive dichiara `CAP_REAL_TIME_STREAMING`, e ripiega su SET CD SPEED se SET STREAMING fallisce. Il passo "SET CD SPEED" del Requirement 8.3 va quindi inteso come impostazione della velocità di lettura (vedi "Differenze rispetto al Requirement 8.3").

### SEEK (usato dal Metodo_Nuovo)

Funzione `seek(dev, 0)` in `lib/qpxtransport/qpx_mmc.cpp`.

| Byte | Valore |
|------|--------|
| 0 | `0x2B` (`MMC_SEEK`) |
| 2-5 | LBA, big-endian (qui 0) |
| 9 | flags (qui `0x00`, default dichiarato in `qpx_mmc.h`) |

LEN = 10, DATA = NONE, 0 byte.

### `cmd_cd_errc_init` (selettore)

File `qscan_cmd.cpp`. Non invia CDB propri.

1. Se `dev->liteon_force_old` è vero: stampa un messaggio, imposta `cd_errc_new = false` ed esegue `cmd_cd_errc_init_old()`.
2. Altrimenti imposta `cd_errc_new = true` ed esegue `cmd_cd_errc_init_new()`. Se questa fallisce (ritorna 1), esegue `cmd_cd_errc_init_old()` e restituisce il suo esito. Il fallback scatta per qualunque errore del SEEK o del probe `F3 0E`, non solo per un timeout.

### `cmd_cd_errc_init_new` (Metodo_Nuovo)

1. SEEK a LBA 0 (tabella sopra). Se fallisce ritorna 1 (senza modificare `cd_errc_new`).
2. Probe ERRC:

| Byte | Valore |
|------|--------|
| 0 | `0xF3` |
| 1 | `0x0E` |
| 11 | `0x00` |

LEN = 12, DATA = READ, 0x10 (16) byte in `rd_buf`. L'esito è solo "comando riuscito o no": il contenuto non è letto. Se fallisce imposta `cd_errc_new = false` e ritorna 1.

### `cmd_cd_errc_init_old` (Metodo_Vecchio)

Cinque comandi in sequenza. Ognuno è `DATA = READ, 256 byte`, `cmd[11] = 0x00` (LEN = 12). Un fallimento qualsiasi interrompe la sequenza e ritorna 1. Poiché `cmd[0]` azzera il CDB, il byte 4 vale 0 quando non indicato.

| Passo | Nome nel log | cmd[0] | cmd[1] | cmd[4] |
|-------|--------------|--------|--------|--------|
| A | `LiteOn_init_A_cx_scan` | `0xDF` | `0xA3` | `0x00` |
| B | `LiteOn_init_B_cx_scan` | `0xDF` | `0xA0` | `0x02` |
| C | `LiteOn_init_C_cx_scan` | `0xDF` | `0xA0` | `0x00` |
| D | `LiteOn_init_D_cx_scan` | `0xDF` | `0xA0` | `0x04` |
| E | `LiteOn_init_E_cx_scan` | `0xDF` | `0xA0` | `0x02` |

A fine sequenza il sorgente stampa "using OLD CD ERRC commands" e ritorna 0. Il contenuto dei 256 byte non è usato.

### Lettura dei contatori, Metodo_Nuovo: `cmd_cd_errc_block_new`

Un solo comando per campione:

| Byte | Valore |
|------|--------|
| 0 | `0xF3` |
| 1 | `0x0E` |
| 11 | `0x00` |

LEN = 12, DATA = READ, 0x10 (16) byte. Interpretazione della risposta (`rd_buf`):

| Offset | Campo |
|--------|-------|
| 1, 2, 3 | posizione M, S, F come interi binari (non BCD). `lba = M*60*75 + S*75 + F` (nessuna sottrazione di 150) |
| 4-5 | BLER (big-endian) |
| 6-7 | E22 (big-endian) |

Il sorgente imposta E11, E21, E31, E12, E32 e UNCR a 0. Con il Metodo_Nuovo il drive non fornisce quindi un valore di E32 (CU) né di UNCR: il sorgente li fissa a 0.

### Lettura dei contatori, Metodo_Vecchio: `cmd_cd_errc_block_old`

Tre comandi per campione, tutti con `DATA = READ, 256 byte` e `cmd[11] = 0x00` (LEN = 12):

| Passo | Funzione | cmd[0] | cmd[1] | cmd[2] | Scopo |
|-------|----------|--------|--------|--------|-------|
| 1 | `cmd_cd_errc_read` | `0xDF` | `0x82` | `0x09` | avvia la misura dell'intervallo |
| 2 | `cmd_cd_errc_getdata` | `0xDF` | `0x82` | `0x05` | legge i contatori |
| 3 | `cmd_cd_errc_inteval_rst` | `0xDF` | `0x97` | `0x00` | azzera l'intervallo |

Interpretazione della risposta del passo 2 (`rd_buf`):

| Offset | Campo |
|--------|-------|
| 0-1 | BLER (big-endian) |
| 2-3 | E22 (big-endian) |
| 4 | E32 (un byte) |

Dopo i tre comandi `lba += 75` (l'intervallo è sintetico: 75 settori per campione). Un errore in uno dei tre passi interrompe il blocco.

### `cmd_cd_errc_end`

| Metodo | Comportamento |
|--------|---------------|
| Nuovo (`cd_errc_new == true`) | non invia nulla, ritorna 0 |
| Vecchio (`cd_errc_new == false`) | CDB `DF A3 01` (cmd[0]=`0xDF`, cmd[1]=`0xA3`, cmd[2]=`0x01`), nessun `cmd[11]`: LEN = **10**, DATA = READ, 256 byte. Se fallisce ritorna 1 |

## Flusso di scansione nel sorgente

Da `qscan_plugin.cpp` (`start_test`, `scan_block`, `end_test`) e `qpx_scan_algo.cpp` (`run_cd_errc`):

1. (opzionale, solo drive HL-DT-ST con `hldtst_test_mode`) toggle test mode: `F8 0F`, DATA NONE. Non rilevante per Lite-On.
2. `probe_drive()` (a livello di drive, non per scansione): per un CD esegue `cmd_cd_errc_init()` poi `cmd_cd_errc_end()`. Esito ok = `DEV_PROBED`.
3. `start_test(CHK_ERRC_CD, lba, speed)`: `lba = ilba`, `set_read_speed(speed)` (SET STREAMING o SET CD SPEED, poi rilettura delle velocità), poi `cmd_cd_errc_init()`. Se l'init fallisce chiama `end_test()`.
4. Il chiamante esegue `wait_unit_ready`, poi cicla `scan_block()` fino a `lba_end` o a stop.
   - Metodo_Nuovo: ogni chiamata è un poll `F3 0E`. Il drive scansiona da solo. Se `lba` non avanza rispetto alla chiamata precedente, il campione è scartato ("stale") e dopo 64 poll consecutivi senza avanzamento il chiamante termina.
   - Metodo_Vecchio: ogni chiamata esegue read, getdata e reset dell'intervallo, e avanza `lba` di 75.
5. `end_test()`: chiama `cmd_cd_errc_end()` e azzera `test`. Il chiamante lo invoca sempre dopo il ciclo, anche su stop o errore di blocco.

## Conferma del flusso del Requirement 8.3

Il flusso "Probe, SET CD SPEED, `cmd_cd_errc_init`, ciclo di lettura con contatori, `cmd_cd_errc_end`" è confermato nell'ordine. Differenze da recepire nell'implementazione:

1. **Probe**. `probe_drive()` esegue anch'esso init e end. Per la scansione il C# può limitarsi a `Probe()` su vendor/modello e lasciare che l'init della scansione faccia da test del Metodo_Nuovo. Il probe del sorgente non è obbligatorio per avviare la scansione (il chiamante lo salta per i drive "listed").
2. **SET CD SPEED**. Il sorgente imposta la velocità con SET STREAMING se disponibile, e con SET CD SPEED (`BB`) in caso contrario. Il progetto usa SET CD SPEED (`MmcCommands`, task 3.3): è un sottoinsieme valido, ma non identico.
3. **CU nel Metodo_Nuovo**. `cmd_cd_errc_block_new` non legge E32: C1 = BLER, C2 = E22, CU = 0 fisso. Il valore non è una misura. **Decisione dell'utente (task 13.4): CU è "non disponibile" (n/d), non 0.** Vedi la sezione "CU non disponibile (n/d)" più sotto.
4. **Mappatura C1/C2/CU**. I file del sorgente usano i nomi BLER, E22, E32. La mappatura BLER = C1, E22 = C2, E32 = CU è la convenzione del progetto (Requirement 8.12): non è dichiarata in questi due file.
5. **Granularità del campione**. Con il Metodo_Vecchio un campione corrisponde a un intervallo di 75 settori, quindi a 1 secondo di disco (coerente con 8.12). Con il Metodo_Nuovo la posizione è quella riportata dal drive (M:S:F) a ogni poll. I campioni vanno aggregati o ricampionati per ottenere uno ogni 75 settori, e i poll senza avanzamento vanno scartati.
6. **Fallback**. Nel sorgente il passaggio Nuovo → Vecchio avviene dentro `cmd_cd_errc_init` per qualunque errore del SEEK o del probe `F3 0E`. Il Requirement 27, criterio 2, lo limita a sense di errore o timeout e a un solo tentativo: è un sottoinsieme del comportamento del sorgente e quindi compatibile.
7. **Stato del flag dopo il fallback**. Nel sorgente, se il SEEK iniziale fallisce, `cd_errc_new` resta `true` anche se subito dopo viene eseguito `cmd_cd_errc_init_old()`. Le letture seguenti userebbero allora `F3 0E` invece di `DF 82 ...`. Il C# non deve replicare il difetto: il metodo effettivo va fissato esplicitamente a Vecchio quando si ripiega.
8. **Fine scansione**. `cmd_cd_errc_end` è un no-op per il Metodo_Nuovo. Il Requirement 8.6 (esecuzione in `finally` con timeout 30 s) resta valido: per il Metodo_Nuovo la fine non invia comandi, per il Vecchio invia `DF A3 01`.

## Metodo_Nuovo e Metodo_Vecchio (`LITEON_FORCE_OLD`)

| Aspetto | Metodo_Nuovo (predefinito) | Metodo_Vecchio |
|---------|----------------------------|----------------|
| Selezione | predefinita | `--liteon-force-old` o variabile d'ambiente `LITEON_FORCE_OLD=1` (`console/qscan/qscan.cpp`), che imposta `dev->liteon_force_old` |
| Init | SEEK LBA 0 + probe `F3 0E` (16 byte) | 5 comandi `DF A3` / `DF A0` (256 byte) |
| Lettura | 1 comando `F3 0E` per poll, il drive avanza da solo | 3 comandi per campione: `DF 82 09`, `DF 82 05`, `DF 97` |
| Posizione | M:S:F dalla risposta | sintetica, +75 settori per campione |
| Contatori | BLER, E22 (E32 = 0, UNCR = 0) | BLER, E22, E32 (UNCR = 0) |
| Fine | nessun comando | `DF A3 01` |
| Fallback | a Vecchio se SEEK o probe falliscono | nessuno |

Nel sorgente `LITEON_FORCE_OLD` è un'opzione del chiamante (riga di comando o variabile d'ambiente). Nel C# diventa l'impostazione "variante Lite-On" (Requirement 8.4 e Impostazioni).

## CU non disponibile (n/d)

Decisione dell'utente al task 13.4: con il Metodo_Nuovo il drive non fornisce E32, quindi CU non va rappresentato come 0.

Scelta tecnica (la più piccola e coerente): `ScanSample.CU` è `int?`. `null` significa "CU non misurato". Non serve un flag negli scanner o nelle capabilities, perché il valore è già per campione, come `C1` (che è `int?` per lo stesso motivo).

| Livello | Comportamento |
|---------|---------------|
| `LiteOnScanner` | Metodo_Nuovo: `CU = null`. Metodo_Vecchio: `CU = E32` (≥ 0). |
| `StandardC2Scanner`, `SampleAggregator` | invariati: `CU = 0` (la vista mostra già "n/d" per i totali CU dello StandardC2). |
| `Evaluator` | un CU `null` non contribuisce al totale CU: non genera KO e non è un errore. Le regole non cambiano per i campioni con CU misurato. |
| `ScanSummary.CuTotal` | `long?`. `null` se nessun campione ha CU misurato; altrimenti somma dei soli CU non nulli. |
| `ChartDownsampler` | il massimo di un intervallo ignora i `null`; il risultato è `null` solo se tutti i valori sono `null`. |
| `ChartSampleMapper` | invariato: `ChartSample.CU` era già `double?`, quindi la serie CU ha interruzioni, il tooltip mostra "n/d" e le statistiche di selezione ignorano i nulli. |
| `ScanViewModel` | il totale CU mostra "n/d" quando `CuTotal` è `null`. |
| Database | migrazione `002_scan_samples_cu_nullable.sql`: `scan_samples.cu` passa da `INTEGER NOT NULL` a `INTEGER` (tabella ricostruita, dati copiati senza modifiche). `scans.cu_total` era già nullable. Nessun vincolo CHECK su `cu` nella 001. |

I campioni salvati prima della migrazione 002 con CU = 0 restano 0: erano prodotti dallo StandardC2 oppure, se Lite-On, da un porting non ancora esistente.

## Note di implementazione di `LiteOnScanner` (task 13.4)

- `Probe` non invia comandi: confronta il vendor dell'INQUIRY già letto (`DriveInfoEx.Vendor`) con "LITE-ON", senza distinzione di maiuscole e dopo `Trim`. Dichiara C1, C2 e CU disponibili (CU solo con il Metodo_Vecchio).
- Flusso: SET CD SPEED (`BB`, timeout 60 s; un errore è registrato come avviso e la scansione prosegue) → init (timeout 60 s) → lettura → fine scansione in `finally` (timeout 30 s).
- Metodo effettivo: parte da `ScanOptions.Method` (Nuovo predefinito). Se l'init Nuovo (SEEK o `F3 0E`) fallisce, si ripiega **una sola volta** sul Vecchio e il metodo effettivo diventa Vecchio prima di eseguire l'init vecchio (punto 7). Se anche l'init Vecchio fallisce, `ScanFailedException` con LBA 0 e Sense.
- Fine scansione: Metodo_Nuovo non invia nulla; Vecchio invia `DF A3 01` (10 byte). Se fallisce, `TestModeEndFailedException` (Req. 9.12), anche dopo annullamento o errore di lettura. Dopo un init rifiutato il fallimento del comando di fine è solo registrato nel Log, così l'errore riportato resta quello dell'init.
- Metodo_Vecchio: un campione ogni 75 settori con `DF 82 09`, `DF 82 05`, `DF 97`. C1 = BLER, C2 = E22, CU = E32. `SpeedX` = 75 settori / tempo reale dei tre comandi.
- Metodo_Nuovo: ogni poll `F3 0E` riporta la posizione M:S:F e i contatori. I poll che avanzano si sommano finché si raggiungono 75 settori, poi si pubblica un campione per ogni 75 settori (se un poll supera più soglie, i contatori sono divisi in parti uguali: i totali si conservano). I poll senza avanzamento sono scartati; dopo 64 consecutivi la scansione termina con `ScanFailedException`. Un gruppo residuo di 1-74 settori è pubblicato alla fine (come in StandardC2Scanner). Tra due poll senza avanzamento c'è una pausa di 25 ms (parametro del costruttore).
- Ipotesi da verificare su hardware (R1 in `docs/risks.md`): i contatori di un poll si riferiscono ai settori percorsi dal poll precedente, e l'indirizzo M:S:F è assoluto (LBA 0 = 00:02:00), quindi l'LBA del campione è `MSF − 150`. Il sorgente non dice né l'una né l'altra cosa: il secondo punto è la convenzione Red Book.

## Quanto non è nei file letti

- Il significato dei byte `A3`, `A0`, `82`, `97`, `09`, `05` e dei campi non letti della risposta non è documentato nel sorgente. Il porting deve riprodurre i byte senza interpretarli.
- La tabella dei modelli (`qscan_plugin.h`) indica i drive Lite-On "listed". Il modello iHAS124 e il chipset non sono stati verificati in questi file. Questo non è stato controllato in questo task.
- L'esito dei comandi attraverso il bridge ICY BOX resta in `docs/risks.md` (R1), non verificato su hardware.
