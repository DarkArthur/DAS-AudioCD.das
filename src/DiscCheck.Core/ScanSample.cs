namespace DiscCheck.Core;

/// <summary>
/// One published scan measurement (design: Data Models). C1 is null when the scanner does not measure it.
/// CU is null when the scanner does not measure it ("n/d"): this is the case of the Lite-On Metodo_Nuovo, which
/// does not read E32 (docs/liteon-cdb.md). A null CU is not zero: it is ignored by totals and verdict.
/// </summary>
public record ScanSample(int Lba, int TimeSeconds, int? C1, int C2, int? CU, double? SpeedX);
