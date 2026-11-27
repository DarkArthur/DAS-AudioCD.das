namespace DAS.Design.Charts;

/// <summary>Statistiche di una metrica sui soli campioni non nulli; i valori sono null se Count = 0 ("n/d").</summary>
public record SelectionStats(double? Min, double? Max, double? Mean, double? Sum, int Count);

public record SelectionResult(double T0, double T1, SelectionStats C1, SelectionStats C2, SelectionStats CU, SelectionStats SpeedX);
