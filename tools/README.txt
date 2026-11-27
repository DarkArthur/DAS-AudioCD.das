DiscCheck - script di download (cartella tools)
==============================================

Due script PowerShell scaricano DiscCheck dalle release di GitHub e verificano
l'integrita' del file con SHA-256. Entrambi scaricano SEMPRE l'ULTIMA versione
pubblicata, a meno che indichi una versione con -Version.

REQUISITI
  - PowerShell 7 (pwsh)
  - GitHub CLI (gh), installato e autenticato una volta:   gh auth login
    (il repository e' pubblico: basta un account GitHub qualsiasi; in alternativa
     si scarica dal browser: https://github.com/DarkArthur/DAS-AudioCD.das/releases/latest)

SCRIPT
  1) download-installer.ps1
     Scarica il programma di installazione guidata:
         DiscCheck-Setup-X.Y.Z-win-x64.exe
     Poi lo si esegue con doppio clic (richiede amministratore).

  2) download-app.ps1
     Scarica l'applicazione senza installazione (zip autonomo) e la estrae in:
         <Destination>\DiscCheck-X.Y.Z\DiscCheck.App.exe
     Lo zip scaricato viene eliminato (usa -KeepZip per tenerlo).
     L'app richiede amministratore all'avvio (accesso al lettore ottico).

PARAMETRI (uguali per i due script)
  -Version      Versione da scaricare, ad es. 1.1.0 (anche "v1.1.0").
                Se omesso: ultima release pubblicata.
  -Destination  Cartella di destinazione. Default: Download dell'utente.
  -Repo         Repository GitHub. Default: DarkArthur/DAS-AudioCD.das
  -KeepZip      (solo download-app.ps1) Conserva lo zip dopo l'estrazione.

ESEMPI
  Ultima versione, nella cartella Download:
      pwsh tools/download-installer.ps1
      pwsh tools/download-app.ps1

  Versione precisa, in una cartella scelta:
      pwsh tools/download-installer.ps1 -Version 1.1.0 -Destination C:\Temp
      pwsh tools/download-app.ps1 -Version 1.1.0 -Destination C:\Tools -KeepZip

COSA FANNO, IN ORDINE
  1. Determinano la versione (ultima release, oppure quella indicata).
  2. Scaricano il file e il relativo .sha256 con "gh release download".
  3. Calcolano lo SHA-256 del file scaricato e lo confrontano con quello
     pubblicato: se non coincide, lo script termina con errore.
  4. Stampano "OK <percorso>" quando e' tutto corretto.

PROBLEMI COMUNI
  - "GitHub CLI (gh) not found": installa gh e riapri il terminale.
  - Errori di autenticazione: esegui "gh auth login".
  - Download bloccato dal SOC/antivirus: chiedi l'autorizzazione del file
    usando lo SHA-256 del file .sha256 pubblicato nella release.
  - Avviso SmartScreen all'esecuzione dell'installer: non e' firmato; scegli
    "Ulteriori informazioni" e poi "Esegui comunque".

NOTIFICA DI NUOVA VERSIONE (nell'app)
  All'avvio l'app controlla l'ultima release pubblica (version.json allegato alla release)
  e, se c'e' una versione piu' recente, mostra un avviso con 'Apri pagina di download'.
  Non scarica ne' installa nulla da sola. Si disattiva svuotando, in Impostazioni,
  il campo 'Indirizzo degli aggiornamenti'; li' si puo' anche indicare un indirizzo interno.
