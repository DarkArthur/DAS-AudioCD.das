# Porting di QPxTool: licenza e distribuzione

I file `src/DiscCheck.Scanners/LiteOnQscanCmd.cs` e `src/DiscCheck.Scanners/MmcSeekPort.cs` sono porting in C# di codice di QPxTool (`github.com/speed47/qpxtool`, commit `9c6a3fdb5e27`), rilasciato sotto GPL-2.0 or later. Ogni file riporta l'intestazione originale (nome del file, copyright, licenza) e una nota di modifica (Req. 25.7).

Stato (D2, risolta come "b" in `docs/decisions.md`): uso personale, senza distribuzione. In questa condizione il testo della GPL-2.0 non è incluso nel repository.

**Se si distribuisce l'applicativo** (binari o sorgenti, anche a terzi):

1. Serve passare a D2 opzione (a) e aggiornare `docs/decisions.md`.
2. Il testo completo della GPL-2.0 va incluso nella distribuzione (Req. 25.8).
3. Le intestazioni dei file portati restano invariate e l'opera derivata va resa disponibile con la stessa licenza, sorgenti compresi.
4. Va verificata la compatibilità con le altre licenze del progetto (D5, LGPL di FLAKE per D1).
