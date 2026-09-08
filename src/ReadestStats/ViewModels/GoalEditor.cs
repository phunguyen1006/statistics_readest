using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed class GoalEditor : ObservableObject
{
    private GoalProgress _progress;
    private GoalHistory _history;
    private double _targetAmount;
    public GoalDefinition Definition { get; }
    public GoalEditor(GoalDefinition definition, GoalProgress progress, GoalHistory history)
    {
        Definition = definition; _progress = progress; _history = history; _targetAmount = ToDisplay(definition);
    }
    public string Period => Definition.Period.ToString().ToUpperInvariant();
    public string MetricLabel => Definition.Metric switch { GoalMetric.Books => "Books finished", GoalMetric.ActiveDays => "Active days", GoalMetric.Sessions => "Sessions", _ => "Reading time" };
    public string Unit => Definition.Metric == GoalMetric.ReadingTime ? "minutes per day" : "books";
    public bool Enabled { get => Definition.Enabled; set { Definition.Enabled = value; Raise(); } }
    public double TargetAmount { get => _targetAmount; set => Set(ref _targetAmount, Math.Max(0, value)); }
    public GoalProgress Progress { get => _progress; set => Set(ref _progress, value); }
    public GoalHistory History { get => _history; set => Set(ref _history, value); }
    public void Commit() => Definition.TargetValue = Definition.Metric == GoalMetric.ReadingTime ? TargetAmount * 60 : TargetAmount;
    private static double ToDisplay(GoalDefinition d) => d.Metric == GoalMetric.ReadingTime ? d.TargetValue / 60 : d.TargetValue;
}
