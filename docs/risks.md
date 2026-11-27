# Registro dei rischi

## R1. Comandi vendor Lite-On attraverso il bridge USB ICY BOX

Rischio: il bridge USB-SATA potrebbe rifiutare o filtrare i CDB vendor (opcode `0xF3`, `0xDF`), rendendo impossibili le scansioni di qualità (M6).

| Comando | Sorgente del CDB | Esito via bridge | Data |
|---------|------------------|------------------|------|
| `cmd_cd_errc_init` (variante "new": SEEK(10) `2B` LBA 0, poi `F3 0E` con 12 byte di CDB e lettura di 0x10 byte) | `plugins/liteon/qscan_cmd.cpp` (`cmd_cd_errc_init_new`) e `lib/qpxtransport/qpx_mmc.cpp` (`seek`), fork speed47/qpxtool, ramo `master` | **non verificato** (esecuzione richiede iHAS124 + ICY BOX, un disco e privilegi di amministratore; non eseguita in questa sessione) | 2026-11-26 |

Note:
- Il CDB è stato letto dal sorgente, non dedotto. Il test è `M1HardwareTests.Vendor_cd_errc_init_probe_via_bridge_reports_outcome` (categoria `Hardware`); stampa "ACCETTATO"/"RIFIUTATO" nell'output.
- Alla prima esecuzione su hardware, aggiornare la colonna "Esito" con `accettato` o `rifiutato` e la data.
- Fine scansione: nel sorgente `cmd_cd_errc_end` non invia nulla per la variante "new" (`if (cd_errc_new) return 0;`); la variante "old" termina con `DF A3 01`. Il test chiude comunque in `finally` con TEST UNIT READY. Se il probe "new" fosse rifiutato, QPxTool ripiega sulla variante "old" (`cmd_cd_errc_init_old`), non coperta da questo test; va valutata in M6 (task 13.3).
- Il probe SEEK ha `flags = 0` (default dichiarato in `lib/qpxtransport/include/qpx_mmc.h`); il valore dell'opcode `MMC_SEEK` (0x2B) deriva dalla tabella dei nomi in `qpx_mmc.cpp`.

## R2. Hardware test M5 (cache e verifica bit-perfect)

Test in `tests/DiscCheck.Hardware.Tests/M5HardwareTests.cs`, categoria `Hardware`, esclusi da `dotnet test --filter "Category!=Hardware"`. Non vengono eseguiti in CI né nelle sessioni automatiche: richiedono Windows, un prompt con privilegi di amministratore, il Lite-On iHAS124 (ICY BOX) e un disco inserito. Se manca un prerequisito il test risulta "skipped" con il motivo.

| Test | Cosa verifica | Prerequisiti | Requisiti |
|------|---------------|--------------|-----------|
| `Cache_detection_on_iHAS124_matches_2048_KB_reference` | `CacheDetectorRunner` completa, `cache_kb` entro 1.536-2.560 KB (riferimento 2.048 KB), nessun avviso | CD audio ≥ 28.500 settori inserito | 12.10, 27.6 |
| `Bit_perfect_verification_of_known_disc_is_identical` | `Verifier` dà IDENTICAL su tutte le tracce di un disco noto | CD audio inserito, Sorgente del disco, offset | 27.6, 27.11 |

Variabili d'ambiente (solo per il test bit-perfect):

| Variabile | Significato |
|-----------|-------------|
| `DISCCHECK_HW_SOURCE` | File o cartella della Sorgente (FLAC/WAV, un file per traccia) del disco inserito |
| `DISCCHECK_HW_OFFSET` | Offset in campioni (da -5000 a 5000) da applicare, cioè il Combined_Offset della coppia drive/disco |

Lancio (PowerShell elevato, dalla radice del repository):

```powershell
# solo cache
dotnet test tests/DiscCheck.Hardware.Tests --filter "FullyQualifiedName~M5HardwareTests.Cache"

# verifica bit-perfect
$env:DISCCHECK_HW_SOURCE = "D:\Rip\DiscoNoto"
$env:DISCCHECK_HW_OFFSET = "6"
dotnet test tests/DiscCheck.Hardware.Tests --filter "FullyQualifiedName~M5HardwareTests.Bit_perfect"

# tutti gli hardware test
dotnet test --filter "Category=Hardware"
```

Il test cache salva solo in un repository in memoria: il database dell'utente non viene toccato. Esito delle esecuzioni su hardware: **non ancora eseguito**. Registrare qui data, `cache_kb` misurato e esito della verifica alla prima esecuzione.

## R3. Hardware test M6 (scansione LiteOnScanner a confronto con QPxTool/Opti Drive Control)

Test in `tests/DiscCheck.Hardware.Tests/M6HardwareTests.cs`, categoria `Hardware`, esclusi da `dotnet test --filter "Category!=Hardware"`. Non vengono eseguiti in CI né nelle sessioni automatiche: richiedono Windows, un prompt con privilegi di amministratore, il Lite-On iHAS124 (ICY BOX), un disco inserito e i totali di riferimento. Se manca un prerequisito il test risulta "skipped" con il motivo.

| Test | Cosa verifica | Requisiti |
|------|---------------|-----------|
| `Full_scan_Metodo_Nuovo_matches_reference_totals_and_closes_test_mode` | Scansione completa con Metodo_Nuovo: totale C1 entro ±10% del riferimento, totale C2 uguale, TEST UNIT READY riuscito dopo la scansione (Test_Mode chiuso), nessuna `TestModeEndFailedException` | 23.7, 27.7, 27.11 |
| `Full_scan_Metodo_Vecchio_matches_reference_totals_and_closes_test_mode` | Come sopra con Metodo_Vecchio (`LITEON_FORCE_OLD`) | 23.7, 27.7, 27.11 |

Procedura: scansionare lo stesso disco, alla stessa velocità, con QPxTool o Opti Drive Control, annotare i totali C1 e C2 dell'intero disco e impostarli nelle variabili.

| Variabile | Significato |
|-----------|-------------|
| `DISCCHECK_HW_REF_C1` | Totale C1 (BLER) del disco misurato da QPxTool/Opti Drive Control (obbligatoria) |
| `DISCCHECK_HW_REF_C2` | Totale C2 (E22) del disco misurato da QPxTool/Opti Drive Control (obbligatoria) |
| `DISCCHECK_HW_SCAN_SPEED` | Velocità di scansione in kB/s (1x = 176); facoltativa, predefinita 704 (4x). Usare la stessa velocità del riferimento |

Lancio (PowerShell elevato, dalla radice del repository):

```powershell
$env:DISCCHECK_HW_REF_C1 = "1234"
$env:DISCCHECK_HW_REF_C2 = "0"
$env:DISCCHECK_HW_SCAN_SPEED = "704"
dotnet test tests/DiscCheck.Hardware.Tests --filter "FullyQualifiedName~M6HardwareTests"

# solo Metodo_Nuovo
dotnet test tests/DiscCheck.Hardware.Tests --filter "FullyQualifiedName~M6HardwareTests.Full_scan_Metodo_Nuovo"
```

Note: con un riferimento C1 pari a 0 la tolleranza è di 1 errore. Il Metodo_Nuovo non misura CU. Esito delle esecuzioni su hardware: **non ancora eseguito**. Registrare qui data, totali misurati e riferimenti alla prima esecuzione.
