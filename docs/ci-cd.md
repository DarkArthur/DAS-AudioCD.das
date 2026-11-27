# CI/CD

```
feature/* --PR--> development --(auto PR + auto-merge)--> main --release-please--> Release PR --merge--> tag + GitHub Release
                      |                                                                                   |
                 image :dev                                    app win-x64 (asset) + image :X.Y.Z -> promozione :latest/:X.Y/:X
                                                                                                          |
                                                                                    PR di sync main -> development
```

| Workflow | Evento | Effetto |
|---|---|---|
| `ci.yml` | PR verso `development` o `main` | titolo PR (Conventional Commits), branch sorgente, build e test senza hardware |
| `promote-to-main.yml` | push su `development` | apre PR `development` -> `main` e abilita auto-merge (merge commit) |
| `image-dev.yml` | push su `development` (docker/**) | immagine CI su GHCR `:dev`, `:dev-<sha>` |
| `release.yml` | push su `main` | release-please; se rilascia: app Windows come asset, immagine `:X.Y.Z`, promozione `latest`/`X.Y`/`X`, PR di sync `main` -> `development` |

La CI esegue `dotnet test --filter "Category!=Hardware&Category!=Performance"`. I test di prestazione dei grafici (`Category=Performance`, `tests/DAS.Design.Charts.Tests/ChartPerformanceTests.cs`) dipendono dai tempi della macchina e si lanciano a mano:

```powershell
dotnet test tests/DAS.Design.Charts.Tests --filter "Category=Performance"
$env:DAS_PERF_TOLERANCE = "3"   # opzionale: moltiplica le soglie in ms (default 1); il limite di 30 ridisegni/s non cambia
```

Immagine: `ghcr.io/<owner>/<repo>-ci`. Contiene il toolchain per build e test; l'app Windows non gira in container.

## Impostazioni del repository (una tantum)

1. Creare il branch `development` da `main`.
2. Settings > General: abilitare **Allow auto-merge**; consentire merge commit.
3. Settings > Actions > General: **Read and write permissions** e **Allow GitHub Actions to create and approve pull requests**.
4. (Richiede piano Pro o repo pubblico) Branch protection su `main` e `development`: PR obbligatoria, check richiesti `PR title`, `Build and test` (e `Source branch` su `main`).
5. Secret `AUTOMATION_TOKEN` (PAT fine-grained o GitHub App: contents + pull requests write). Con il solo `GITHUB_TOKEN` le PR create dai workflow non avviano la CI e restano bloccate dai check richiesti.
6. Environment `production` (opzionale): aggiungere reviewer per approvare la promozione dei tag.
7. Pacchetto GHCR: al primo push collegarlo al repository e impostarne la visibilita'.

## Regole di commit

- PR con squash verso `development`: titolo in Conventional Commits (`feat:`, `fix:`, `feat!:` ...).
- `development` -> `main` con merge commit, per conservare i commit letti da release-please.
- La release si crea fondendo la PR "chore(main): release X.Y.Z" aperta da release-please (passaggio manuale volontario).

Senza branch protection (repo privato, piano Free) l'auto-merge non esiste: i workflow attendono i check della PR con `gh pr checks --watch` e poi fondono.

## Installer guidato (Windows)

Ogni release allega anche `DiscCheck-Setup-X.Y.Z-win-x64.exe` (+ `.sha256`), creato con Inno Setup 6 da `installer/DiscCheck.iss`. Procedura guidata in italiano e inglese, richiede privilegi di amministratore (come l'app), crea il collegamento nel menu Start (e sul desktop, facoltativo) e il disinstallatore. Un aggiornamento sovrascrive la versione precedente. Le impostazioni, il Database e i log dell'utente restano al disinstallo.

Build locale: `winget install JRSoftware.InnoSetup` e poi `pwsh installer/build-installer.ps1 -Version 1.0.1` (output in `artifacts/`). Non esegue test di installazione: va provato a mano.

