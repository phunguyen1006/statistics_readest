using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
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

    protected override AutomationPeer OnCreateAutomationPeer() => new StatCardAutomationPeer(this);

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

    private sealed class StatCardAutomationPeer(StatCard owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override string GetClassNameCore() => nameof(StatCard);
        protected override AutomationControlType GetAutomationControlTypeCore() => ((StatCard)Owner).IsInteractive ? AutomationControlType.Button : AutomationControlType.Text;
        protected override string GetNameCore()
        {
            var card = (StatCard)Owner;
            var suppliedName = AutomationProperties.GetName(card);
            return !string.IsNullOrWhiteSpace(suppliedName) ? suppliedName :
                string.Join(". ", new[] { card.Label, card.Value, card.Detail }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }
        protected override string GetHelpTextCore()
        {
            var help = base.GetHelpTextCore();
            return string.IsNullOrWhiteSpace(help) ? ((StatCard)Owner).Help ?? "" : help;
        }
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke && ((StatCard)Owner).IsInteractive ? this : base.GetPattern(patternInterface);
        public void Invoke()
        {
            var card = (StatCard)Owner;
            card.Dispatcher.Invoke(() =>
            {
                if (!card.IsEnabled || !card.IsInteractive || card.Command?.CanExecute(card.CommandParameter) != true)
                    throw new ElementNotEnabledException();
                card.Command.Execute(card.CommandParameter);
            });
        }
    }
}
