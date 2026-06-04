using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using SerialPortTool.Helpers;
using SerialPortTool.Models;
using SerialPortTool.ViewModels;

namespace SerialPortTool.Views;

public partial class SerialPortControl : UserControl
{
    private DateTime _lastScrollTime = DateTime.MinValue;
    private readonly TimeSpan _scrollInterval = TimeSpan.FromMilliseconds(200);
    private const double DrawerMinWidth = 300;
    private const double DrawerMaxWidth = 640;
    private const double DrawerDefaultWidth = 400;

    public SerialPortControl()
    {
        InitializeComponent();
        EnsureDrawerClosed();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SerialPortViewModel vm)
        {
            vm.RequestScrollToEnd += (_, _) =>
            {
                var now = DateTime.Now;
                if (now - _lastScrollTime < _scrollInterval) return;
                _lastScrollTime = now;

                Dispatcher.BeginInvoke(() =>
                {
                    if (RawLogListBox.Items.Count > 0)
                        RawLogListBox.ScrollIntoView(RawLogListBox.Items[^1]);
                    if (FilteredLogListBox.Items.Count > 0)
                        FilteredLogListBox.ScrollIntoView(FilteredLogListBox.Items[^1]);
                }, System.Windows.Threading.DispatcherPriority.Background);
            };
        }
    }

    #region Drawer
    private double GetSavedDrawerWidth()
    {
        if (DataContext is SerialPortViewModel vm && vm.PresetDrawerWidth >= DrawerMinWidth)
            return Math.Clamp(vm.PresetDrawerWidth, DrawerMinWidth, DrawerMaxWidth);
        return DrawerDefaultWidth;
    }

    private void EnsureDrawerClosed()
    {
        TogglePresetDrawerButton.IsChecked = false;
        SetDrawerOpen(false);
    }

    private void SetDrawerOpen(bool open)
    {
        PresetDrawerSplitter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        PresetDrawer.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

        if (open)
        {
            PresetDrawerColumn.MinWidth = DrawerMinWidth;
            PresetDrawerColumn.Width = new GridLength(GetSavedDrawerWidth());
        }
        else
        {
            PresetDrawerColumn.MinWidth = 0;
            PresetDrawerColumn.Width = new GridLength(0);
        }
    }

    private void TogglePresetDrawer_Checked(object sender, RoutedEventArgs e) =>
        SetDrawerOpen(true);

    private void TogglePresetDrawer_Unchecked(object sender, RoutedEventArgs e) =>
        SetDrawerOpen(false);

    private void ClosePresetDrawer_Click(object sender, RoutedEventArgs e)
    {
        TogglePresetDrawerButton.IsChecked = false;
    }

    private void PresetCommandTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: PresetCommandItem item })
            item.FinalizeHexCommand();
    }

    private void PresetDrawerSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (PresetDrawerColumn.ActualWidth < DrawerMinWidth) return;
        double width = Math.Clamp(PresetDrawerColumn.ActualWidth, DrawerMinWidth, DrawerMaxWidth);
        PresetDrawerColumn.Width = new GridLength(width);
        if (DataContext is SerialPortViewModel vm)
            vm.PresetDrawerWidth = width;
    }

    private void ApplyFilterButton_Click(object sender, RoutedEventArgs e)
    {
        FilterRegexTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (DataContext is SerialPortViewModel vm)
            vm.ApplyFilter();
    }
    #endregion
}

// ===================== 转换器 =====================
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool b) return !b;
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool b) return !b;
        return true;
    }
}

public class DirectionColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is string dir)
            return dir == "TX"
                ? new SolidColorBrush(Color.FromRgb(46, 125, 50))   // TX 深绿
                : new SolidColorBrush(Color.FromRgb(21, 101, 192));  // RX 深蓝
        return new SolidColorBrush(Color.FromRgb(51, 51, 51));
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

// ===================== 高亮文本 =====================
public class HighlightTextBlock : TextBlock
{
    private static readonly (Color Background, Color Foreground)[] MultiHighlightStyles =
    {
        (Color.FromRgb(255, 215, 0), Colors.Black),
        (Color.FromRgb(173, 216, 230), Colors.Black),
        (Color.FromRgb(152, 251, 152), Colors.Black),
        (Color.FromRgb(255, 182, 193), Colors.Black),
        (Color.FromRgb(221, 160, 221), Colors.Black),
        (Color.FromRgb(255, 228, 181), Colors.Black),
        (Color.FromRgb(176, 224, 230), Colors.Black),
        (Color.FromRgb(255, 160, 122), Colors.Black),
    };

    public static readonly DependencyProperty SourceTextProperty =
        DependencyProperty.Register(nameof(SourceText), typeof(string), typeof(HighlightTextBlock),
            new PropertyMetadata("", OnHighlightPropertyChanged));

    public static readonly DependencyProperty RegexPatternProperty =
        DependencyProperty.Register(nameof(RegexPattern), typeof(string), typeof(HighlightTextBlock),
            new PropertyMetadata("", OnHighlightPropertyChanged));

    public static readonly DependencyProperty BaseForegroundProperty =
        DependencyProperty.Register(nameof(BaseForeground), typeof(Brush), typeof(HighlightTextBlock),
            new PropertyMetadata(Brushes.Black, OnHighlightPropertyChanged));

    private bool _highlightQueued;

    public string SourceText
    {
        get => (string)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    public string RegexPattern
    {
        get => (string)GetValue(RegexPatternProperty);
        set => SetValue(RegexPatternProperty, value);
    }

    public Brush BaseForeground
    {
        get => (Brush)GetValue(BaseForegroundProperty);
        set => SetValue(BaseForegroundProperty, value);
    }

    private static void OnHighlightPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HighlightTextBlock block)
            block.QueueUpdateHighlight();
    }

    private void QueueUpdateHighlight()
    {
        if (_highlightQueued)
            return;

        _highlightQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _highlightQueued = false;
            UpdateHighlight();
        }, System.Windows.Threading.DispatcherPriority.DataBind);
    }

    private void UpdateHighlight()
    {
        string text = SourceText ?? "";
        string pattern = RegexPattern ?? "";
        Brush baseFg = BaseForeground ?? Brushes.White;

        Inlines.Clear();
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(text))
        {
            Inlines.Add(new Run(text) { Foreground = baseFg });
            return;
        }

        try
        {
            var subPatterns = RegexHelper.SplitAlternationPatterns(pattern);
            if (subPatterns.Count > 1)
                ApplyMultiPatternHighlight(text, pattern, subPatterns, baseFg);
            else
                ApplySinglePatternHighlight(text, pattern, baseFg);
        }
        catch (RegexMatchTimeoutException)
        {
            Inlines.Add(new Run(text) { Foreground = baseFg });
        }
        catch (ArgumentException)
        {
            Inlines.Add(new Run(text) { Foreground = baseFg });
        }
    }

    private void ApplySinglePatternHighlight(string text, string pattern, Brush baseFg)
    {
        int last = 0;
        bool highlighted = false;
        foreach (var m in RegexHelper.EnumerateMatches(pattern, text))
        {
            highlighted = true;
            if (m.Index > last)
                Inlines.Add(new Run(text.Substring(last, m.Index - last)) { Foreground = baseFg });
            Inlines.Add(CreateHighlightRun(m.Value, MultiHighlightStyles[0]));
            last = m.Index + m.Length;
        }

        if (!highlighted)
            Inlines.Add(new Run(text) { Foreground = baseFg });
        else if (last < text.Length)
            Inlines.Add(new Run(text.Substring(last)) { Foreground = baseFg });
    }

    private void ApplyMultiPatternHighlight(string text, string fullPattern, IReadOnlyList<string> subPatterns, Brush baseFg)
    {
        var spans = new List<(int Index, int Length, int PatternIndex)>();

        for (int pi = 0; pi < subPatterns.Count; pi++)
        {
            string sub = subPatterns[pi];
            if (string.IsNullOrWhiteSpace(sub))
                continue;

            foreach (var m in RegexHelper.EnumerateMatches(sub.Trim(), text))
                spans.Add((m.Index, m.Length, pi));
        }

        if (spans.Count == 0)
        {
            ApplySinglePatternHighlight(text, fullPattern, baseFg);
            return;
        }

        RenderHighlightSpans(text, spans, baseFg);
    }

    private void RenderHighlightSpans(string text, List<(int Index, int Length, int PatternIndex)> spans, Brush baseFg)
    {
        spans = spans
            .Select(s => (s.Index, Math.Min(s.Length, Math.Max(0, text.Length - s.Index)), s.PatternIndex))
            .Where(s => s.Index >= 0 && s.Item2 > 0)
            .ToList();

        if (spans.Count == 0)
        {
            Inlines.Add(new Run(text) { Foreground = baseFg });
            return;
        }

        var boundaries = new SortedSet<int> { 0, text.Length };
        foreach (var (index, length, _) in spans)
        {
            boundaries.Add(index);
            boundaries.Add(index + length);
        }

        var points = boundaries.ToList();
        int last = 0;
        for (int i = 0; i < points.Count - 1; i++)
        {
            int segStart = points[i];
            int segEnd = points[i + 1];
            int segLen = segEnd - segStart;
            if (segLen <= 0)
                continue;

            int? bestPattern = null;
            int bestLen = -1;
            foreach (var (index, length, patternIndex) in spans)
            {
                int end = index + length;
                if (index <= segStart && end >= segEnd)
                {
                    if (length > bestLen)
                    {
                        bestLen = length;
                        bestPattern = patternIndex;
                    }
                }
            }

            if (segStart > last)
                Inlines.Add(new Run(text.Substring(last, segStart - last)) { Foreground = baseFg });

            if (bestPattern.HasValue)
            {
                var style = MultiHighlightStyles[bestPattern.Value % MultiHighlightStyles.Length];
                Inlines.Add(CreateHighlightRun(text.Substring(segStart, segLen), style));
            }
            else
                Inlines.Add(new Run(text.Substring(segStart, segLen)) { Foreground = baseFg });

            last = segEnd;
        }

        if (last < text.Length)
            Inlines.Add(new Run(text.Substring(last)) { Foreground = baseFg });
    }

    private static Run CreateHighlightRun(string value, (Color Background, Color Foreground) style) =>
        new(value)
        {
            Background = new SolidColorBrush(style.Background),
            Foreground = new SolidColorBrush(style.Foreground),
            FontWeight = FontWeights.Bold
        };
}
