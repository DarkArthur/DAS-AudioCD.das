namespace DiscCheck.Scsi.Drives;

/// <summary>User-facing descriptions of SCSI/transport errors (Requirements 2.9, 2.12, 4.6, 4.10).</summary>
public static class ScsiErrorMessages
{
    public const int ErrorAccessDenied = 5;

    public const string AdministratorRequired =
        "Sono richiesti privilegi di amministratore. Chiudere DiscCheck e riavviarlo come amministratore " +
        "(clic destro sull'applicazione, quindi \"Esegui come amministratore\").";

    /// <summary>NOT READY / MEDIUM NOT PRESENT (Key 2, ASC 0x3A): the drive answers but holds no disc.</summary>
    public static bool IsNoDisc(SenseData? sense) => sense is { Key: 0x02, Asc: 0x3A };

    public static string Describe(MmcError error) => error switch
    {
        MmcError.CommandError { Transport: not null } e => Describe(e.Transport),
        MmcError.CommandError e =>
            $"{e.Name}: status 0x{e.Status:X2}" + (e.Sense is { } s ? $", {DescribeSense(s)}" : string.Empty),
        MmcError.ParseError e =>
            $"{e.Command}: risposta troppo corta ({e.ActualLen} byte ricevuti, {e.MinLen} attesi)",
        _ => "errore sconosciuto",
    };

    public static string Describe(TransportError error) => error switch
    {
        TransportError.OpenFailed { ErrorCode: ErrorAccessDenied } => AdministratorRequired,
        TransportError.OpenFailed e =>
            $"impossibile aprire l'unità {(e.Drive is { } d ? $"{d}:" : string.Empty)} (codice di errore Windows {e.ErrorCode})",
        TransportError.Timeout e =>
            "timeout" + (e.Applied is { } t ? $" di {t.TotalSeconds:0.#} s" : string.Empty) +
            (e.Cdb is { } c ? $" (CDB {c})" : string.Empty),
        TransportError.WinError e => $"errore di trasporto (codice di errore Windows {e.Code})",
        _ => "errore di trasporto",
    };

    public static string DescribeSense(SenseData s) => $"Sense Key 0x{s.Key:X}, ASC 0x{s.Asc:X2}, ASCQ 0x{s.Ascq:X2}";

    /// <summary>CDB as hex text for the Log (Requirement 4.10).</summary>
    public static string Hex(ReadOnlySpan<byte> cdb) => Convert.ToHexString(cdb);
}
