using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ReadestStats.Controls;

public sealed class StatCard : Control
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatCard));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(string), typeof(StatCard));
    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(nameof(Detail), typeof(string), typeof(StatCard));
    public static readonly DependencyProperty HelpProperty = DependencyProperty.Register(nameof(Help), typeof(string), typeof(StatCard));
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(StatCard), new PropertyMetadata(null, OnCommandChanged));
    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(StatCard));
    private static readonly DependencyPropertyKey IsInteractivePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsInteractive), typeof(bool), typeof(StatCard), new PropertyMetadata(false));
    public static readonly DependencyProperty IsInteractiveProperty = IsInteractivePropertyKey.DependencyProperty;
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Detail { get => (string)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public string Help { get => (string)GetValue(HelpProperty); set => SetValue(HelpProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
    public bool IsInteractive => (bool)GetValue(IsInteractiveProperty);

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsInteractive || Command is null || !Command.CanExecute(CommandParameter)) return;
        Focus();
        Command.Execute(CommandParameter);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsInteractive || Command is null || !Command.CanExecute(CommandParameter) || (e.Key != Key.Enter && e.Key != Key.Space)) return;
        Command.Execute(CommandParameter);
        e.Handled = true;
    }

    private static void OnCommandChanged(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        var card = (StatCard)source;
        var interactive = args.NewValue is ICommand;
        card.SetValue(IsInteractivePropertyKey, interactive);
        card.Focusable = interactive;
    }
}
