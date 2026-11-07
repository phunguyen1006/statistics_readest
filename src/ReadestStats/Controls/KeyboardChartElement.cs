using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;

namespace ReadestStats.Controls;

/// <summary>Shows the exact focused chart value for sighted keyboard users.</summary>
public abstract class KeyboardChartElement : FrameworkElement
{
    private readonly DrawingVisual _readoutVisual = new();
    private string? _keyboardReadout;
    private bool _keyboardFocusVisible;

    protected KeyboardChartElement() => AddVisualChild(_readoutVisual);
    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => index == 0 ? _readoutVisual : throw new ArgumentOutOfRangeException(nameof(index));
    protected override AutomationPeer OnCreateAutomationPeer() => new ChartAutomationPeer(this);
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); _keyboardFocusVisible = true; InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); _keyboardFocusVisible = false; InvalidateVisual(); }
    protected void ShowKeyboardReadout(string text) => UpdateKeyboardReadout(text);
    protected void HideKeyboardReadout() => UpdateKeyboardReadout(null);

    private void UpdateKeyboardReadout(string? text)
    {
        var previous = _keyboardReadout ?? "";
        _keyboardReadout = text;
        var current = text ?? "";
        if (previous != current && UIElementAutomationPeer.FromElement(this) is ChartAutomationPeer peer)
        {
            if (AutomationPeer.ListenerExists(AutomationEvents.PropertyChanged))
                peer.RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, previous, current);
            if (current.Length > 0 && AutomationPeer.ListenerExists(AutomationEvents.Notification))
                peer.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, current, "ReadestStats.ChartSelection");
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_keyboardFocusVisible && ActualWidth > 2 && ActualHeight > 2)
            dc.DrawRectangle(null, new Pen((Brush)FindResource("Focus"), 1), new Rect(.5, .5, ActualWidth - 1, ActualHeight - 1));
        using var overlay = _readoutVisual.RenderOpen();
        if (string.IsNullOrWhiteSpace(_keyboardReadout) || ActualWidth < 32 || ActualHeight < 24) return;
        const double padding = 7;
        var width = Math.Min(320, ActualWidth - 8);
        var text = new FormattedText(_keyboardReadout, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, (Brush)FindResource("TooltipForeground"), VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, width - padding * 2),
            MaxTextHeight = Math.Max(1, ActualHeight - padding * 2 - 8),
            MaxLineCount = 3,
            Trimming = TextTrimming.CharacterEllipsis
        };
        var height = Math.Min(ActualHeight - 8, text.Height + padding * 2);
        var box = new Rect(Math.Max(4, ActualWidth - width - 4), Math.Max(4, ActualHeight - height - 4), width, height);
        overlay.DrawRoundedRectangle((Brush)FindResource("TooltipBackground"), new Pen((Brush)FindResource("BorderStrong"), 1), box, 4, 4);
        overlay.DrawText(text, new Point(box.Left + padding, box.Top + padding));
    }

    private sealed class ChartAutomationPeer(KeyboardChartElement owner) : FrameworkElementAutomationPeer(owner), IValueProvider
    {
        protected override string GetClassNameCore() => Owner.GetType().Name;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
        protected override string GetLocalizedControlTypeCore() => Localization.Localizer.Instance.Translate("Chart");
        protected override string GetHelpTextCore()
        {
            var instructions = Localization.Localizer.Instance.Translate("Use arrow keys to explore the chart.");
            var help = base.GetHelpTextCore();
            return string.IsNullOrWhiteSpace(help) ? instructions : $"{instructions}\n{help}";
        }
        public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.Value ? this : base.GetPattern(patternInterface);
        public bool IsReadOnly => true;
        public string Value => ((KeyboardChartElement)Owner)._keyboardReadout ?? "";
        public void SetValue(string value) => throw new InvalidOperationException("Chart values are read-only.");
    }
}
