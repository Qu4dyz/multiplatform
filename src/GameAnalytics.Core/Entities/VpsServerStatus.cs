namespace GameAnalytics.Core.Entities;

public class VpsServerStatus
{
    public string Status { get; set; } = "online";
    public string ServerTime { get; set; } = string.Empty;
    public string ActiveAlgorithm { get; set; } = "FastTree";
    public int TotalMatches { get; set; }
    public int TotalTeamStats { get; set; }
    public double Accuracy { get; set; }
    public double AreaUnderRocCurve { get; set; }
    public double F1Score { get; set; }
    /// <summary>Accuracy when |p−0.5| ≥ 0.15 (abstention metric for labs).</summary>
    public double AccuracyWhenConfident { get; set; }
    /// <summary>Share of test games that were confident enough to score.</summary>
    public double ConfidentCoverage { get; set; }
    public double BrierScore { get; set; }
    public bool ModelFileExists { get; set; }
    public long ModelFileSize { get; set; }
    public string LastModelUpdate { get; set; } = string.Empty;
    public int Epoch { get; set; }
}
