using Microsoft.ML.Data;

namespace GameAnalytics.ML.Models;

public class MatchInputData
{
    [LoadColumn(0)]
    public float FirstBlood { get; set; }

    [LoadColumn(1)]
    public float FirstTower { get; set; }

    [LoadColumn(2)]
    public float FirstDragon { get; set; }

    [LoadColumn(3)]
    public float GoldDiff15 { get; set; }

    [LoadColumn(4)]
    public float KillDiff15 { get; set; }

    [LoadColumn(5)]
    public float BlueAvgWinRate { get; set; }

    [LoadColumn(6)]
    public float RedAvgWinRate { get; set; }

    [LoadColumn(7)]
    public float TowerDiff { get; set; }

    [LoadColumn(8)]
    public float DragonDiff { get; set; }

    [LoadColumn(9), ColumnName("Label")]
    public bool Label { get; set; }
}

public class MatchPrediction
{
    [ColumnName("PredictedLabel")]
    public bool PredictedLabel { get; set; }

    public float Probability { get; set; }

    public float Score { get; set; }
}

