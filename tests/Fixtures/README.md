# Fixtures SCSI

File `.bin`: payload di dati (senza CDB) restituiti da `FakeScsiTransport.WhenFixture`.

## Fixture Lite-On (`liteon-*.bin`)

**Sintetiche: non catturate da hardware.** Costruite a mano dai layout in `docs/liteon-cdb.md` (task 13.6). Rappresentano risposte plausibili, non tracce reali di un drive. Vanno sostituite o affiancate da catture reali quando il rischio R1 (`docs/risks.md`) sarà verificato su hardware.

| File | Comando | Dimensione | Contenuto |
|------|---------|-----------:|-----------|
| `liteon-init-old-a.bin` … `-e.bin` | init Vecchio `DF A3` / `DF A0` (A-E) | 256 | zeri (contenuto non usato) |
| `liteon-probe-new.bin` | probe Nuovo `F3 0E` | 16 | zeri (conta solo l'esito) |
| `liteon-block-new.bin` | poll Nuovo `F3 0E` | 16 | M:S:F = 00:02:00 (offset 1-3), BLER = 300 (4-5), E22 = 7 (6-7) |
| `liteon-old-read.bin` | `DF 82 09` | 256 | zeri |
| `liteon-old-getdata.bin` | `DF 82 05` | 256 | BLER = 0x0123 (0-1), E22 = 0x0045 (2-3), E32 = 9 (4) |
| `liteon-old-interval-reset.bin` | `DF 97` | 256 | zeri |
| `liteon-old-end.bin` | `DF A3 01` | 256 | zeri |

Il SEEK e il SET CD SPEED non trasferiscono dati e non hanno fixture.
