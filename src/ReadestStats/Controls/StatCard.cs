using System.Windows;
using System.Windows.Controls;

namespace ReadestStats.Controls;

public sealed class StatCard : Control
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatCard));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(string), typeof(StatCard));
    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(nameof(Detail), typeof(string), typeof(StatCard));
    public static readonly DependencyProperty HelpProperty = DependencyProperty.Register(nameof(Help), typeof(string), typeof(StatCard));
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Detail { get => (string)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public string Help { get => (string)GetValue(HelpProperty); set => SetValue(HelpProperty, value); }
}
