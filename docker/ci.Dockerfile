# Immagine di build/test per la CI (Linux). Non contiene l'app: DiscCheck e' una
# applicazione desktop Windows che richiede amministratore e SCSI pass-through.
# I test marcati Category=Hardware non possono girare qui.
FROM mcr.microsoft.com/dotnet/sdk:11.0

ENV DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    EnableWindowsTargeting=true

LABEL org.opencontainers.image.title="DiscCheck CI" \
      org.opencontainers.image.description=".NET 11 SDK per build e test non-hardware di DiscCheck"

WORKDIR /src
