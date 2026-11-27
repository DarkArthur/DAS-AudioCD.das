# Registro delle decisioni

Ogni decisione riporta: identificativo, opzioni considerate, stato (`aperta` o `risolta`), opzione scelta, data di risoluzione (AAAA-MM-GG) e motivazione (almeno una frase).
Per le decisioni `aperta`, opzione scelta, data e motivazione sono `—` fino alla risoluzione.

| ID | Oggetto | Stato | Bloccante per |
|----|---------|-------|---------------|
| D1 | Decoder FLAC | risolta (a, 2026-11-27) | Nessuno (era bloccante per M5, Req. 25.2, 25.6) |
| D2 | Licenza del porting di QPxTool_Source | risolta (b, 2026-11-27) | Nessuno (era bloccante per M6, Req. 25.3, 25.6). Se in futuro si distribuisce: passare a (a) (Req. 25.7, 25.8) |
| D3 | Nome definitivo dell'applicativo | aperta | Primo rilascio (Req. 25.4) |
| D4 | Masterizzazione integrata in v2 | aperta | Nessuno (la v1 non implementa masterizzazione, Req. 25.5) |
| D5 | Licenza degli asset di DAS_Design | risolta (b, 2026-11-27) | Nessuna per la licenza. La pubblicazione di DAS_Design richiede solo la conferma del titolare in `LICENSE-ASSETS.md` (Req. 36.3, 36.4) |

## D1 - Decoder FLAC

- **Opzioni**:
  - (a) `CUETools.Codecs.FLAKE` (LGPL)
  - (b) `flac.exe` esterno
- **Stato**: risolta
- **Opzione scelta**: (a) `CUETools.Codecs.FLAKE` (LGPL)
- **Data di risoluzione**: 2026-11-27
- **Motivazione**: libreria .NET integrata, nessun eseguibile esterno da distribuire e da individuare a runtime. L'accesso resta isolato dietro `IFlacDecoder` nel Core, quindi la scelta è sostituibile senza modificare il Verifier.
- **Note**: l'accesso al decoder avviene solo tramite `IFlacDecoder` nel Core (Req. 25.9), quindi la scelta è sostituibile senza modificare il Verifier. Conformità LGPL: la libreria è usata come assembly separato e collegato dinamicamente (non incorporato nel Core), ogni distribuzione include il testo della LGPL e l'indicazione della libreria e della sua licenza, così che l'utente possa sostituirla con una versione modificata.

## D2 - Licenza del porting di QPxTool_Source

- **Opzioni**:
  - (a) Distribuzione del porting con licenza GPL-2.0 (obbligatoria in caso di distribuzione)
  - (b) Uso personale, senza distribuzione
- **Stato**: risolta
- **Opzione scelta**: (b) Uso personale, senza distribuzione
- **Data di risoluzione**: 2026-11-27
- **Motivazione**: il porting è per uso personale e non viene distribuito, quindi non c'è un obbligo immediato di rilascio sotto GPL-2.0. Se in futuro si distribuisce, occorre passare all'opzione (a), includendo il testo GPL-2.0 e le intestazioni dei file portati (Req. 25.7, 25.8).
- **Note**: se risolta con (a), ogni distribuzione include il testo completo della GPL-2.0 (Req. 25.8) e ogni file portato riporta l'intestazione richiesta dal Req. 25.7.

## D3 - Nome definitivo dell'applicativo

- **Opzioni**:
  - (a) Mantenere "DiscCheck" (nome di lavoro)
  - (b) Adottare un altro nome (da definire)
- **Stato**: aperta
- **Opzione scelta**: —
- **Data di risoluzione**: —
- **Motivazione**: —
- **Note**: il nome è mostrato accanto al DAS_Logo (Req. 32.2).

## D4 - Masterizzazione integrata in v2

- **Opzioni**:
  - (a) Sì
  - (b) No
- **Stato**: aperta
- **Opzione scelta**: —
- **Data di risoluzione**: —
- **Motivazione**: —
- **Note**: qualunque sia l'esito, la v1 implementa zero funzioni di masterizzazione (Req. 25.5).

## D5 - Licenza degli asset di DAS_Design

- **Opzioni** (Req. 36.2):
  - (a) Stessa licenza del progetto per codice e asset
  - (b) Licenza del progetto per il codice e CC-BY 4.0 per gli asset grafici (logo, icone)
  - (c) CC0 1.0 per gli asset grafici
- **Stato**: risolta
- **Opzione scelta**: (b) Licenza del progetto per il codice e CC-BY 4.0 per gli asset grafici (logo, icone)
- **Data di risoluzione**: 2026-11-27
- **Motivazione**: il codice resta sotto la licenza del progetto, mentre logo e icone possono essere riusati con l'obbligo di attribuzione al titolare, che tutela la paternità dell'identità grafica senza vincolare il codice.
- **Note**: `LICENSE-ASSETS.md` riporta licenza CC-BY 4.0, anno e titolare. Il titolare è ancora `[TITOLARE DA CONFERMARE]`: nel repository non c'è un file LICENSE né un copyright esplicito. L'attribuzione (Req. 36.6) è fornita da `AssetAttribution.CurrentText` e compare nella finestra Informazioni e nel report HTML. DAS_Design non è pubblicato come pacchetto NuGet né come repository pubblico autonomo: la pubblicazione richiede ora solo la conferma del titolare (Req. 36.3). Il font Inter resta sotto SIL OFL 1.1 indipendentemente da D5 (Req. 36.5).