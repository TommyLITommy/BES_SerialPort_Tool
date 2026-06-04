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
    private readonly record struct HighlightSpan(int Index, int Length, int PatternIndex);
    private readonly record struct HighlightSegment(int Start, int Length, int? PatternIndex);

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
        var spans = new List<HighlightSpan>();

        for (int pi = 0; pi < subPatterns.Count; pi++)
        {
            string sub = subPatterns[pi].Trim();
            if (string.IsNullOrWhiteSpace(sub))
                continue;

            foreach (var m in RegexHelper.EnumerateMatches(sub, text))
                spans.Add(new HighlightSpan(m.Index, m.Length, pi));
        }

        if (spans.Count == 0)
        {
            ApplySinglePatternHighlight(text, fullPattern, baseFg);
            return;
        }

        RenderHighlightSpans(text, spans, baseFg);
    }

    private void RenderHighlightSpans(string text, List<HighlightSpan> spans, Brush baseFg)
    {
        spans = spans
            .Select(s => new HighlightSpan(s.Index, Math.Min(s.Length, Math.Max(0, text.Length - s.Index)), s.PatternIndex))
            .Where(s => s.Index >= 0 && s.Length > 0)
            .ToList();

        if (spans.Count == 0)
        {
            Inlines.Add(new Run(text) { Foreground = baseFg });
            return;
        }

        foreach (var segment in BuildHighlightSegments(text.Length, spans))
        {
            string value = text.Substring(segment.Start, segment.Length);
            if (segment.PatternIndex.HasValue)
            {
                var style = MultiHighlightStyles[segment.PatternIndex.Value % MultiHighlightStyles.Length];
                Inlines.Add(CreateHighlightRun(value, style));
            }
            else
            {
                Inlines.Add(new Run(value) { Foreground = baseFg });
            }
        }
    }

    private static Run CreateHighlightRun(string value, (Color Background, Color Foreground) style) =>
        new(value)
        {
            Background = new SolidColorBrush(style.Background),
            Foreground = new SolidColorBrush(style.Foreground),
            FontWeight = FontWeights.Bold
        };

    private static IReadOnlyList<HighlightSegment> BuildHighlightSegments(int textLength, IReadOnlyList<HighlightSpan> spans)
    {
        var boundaries = new SortedSet<int> { 0, textLength };
        foreach (var span in spans)
        {
            boundaries.Add(span.Index);
            boundaries.Add(span.Index + span.Length);
        }

        var points = boundaries.ToList();
        var segments = new List<HighlightSegment>(points.Count - 1);

        for (int i = 0; i < points.Count - 1; i++)
        {
            int start = points[i];
            int end = points[i + 1];
            int length = end - start;
            if (length <= 0)
                continue;

            int? patternIndex = SelectWinningPattern(spans, start, end);
            AppendMergedSegment(segments, start, length, patternIndex);
        }

        return segments;
    }

    private static int? SelectWinningPattern(IReadOnlyList<HighlightSpan> spans, int segmentStart, int segmentEnd)
    {
        HighlightSpan? winner = null;

        foreach (var span in spans)
        {
            int spanEnd = span.Index + span.Length;
            if (span.Index > segmentStart || spanEnd < segmentEnd)
                continue;

            if (winner is null ||
                span.PatternIndex < winner.Value.PatternIndex ||
                (span.PatternIndex == winner.Value.PatternIndex && span.Index < winner.Value.Index) ||
                (span.PatternIndex == winner.Value.PatternIndex && span.Index == winner.Value.Index && span.Length > winner.Value.Length))
            {
                winner = span;
            }
        }

        return winner?.PatternIndex;
    }

    private static void AppendMergedSegment(List<HighlightSegment> segments, int start, int length, int? patternIndex)
    {
        if (segments.Count > 0)
        {
            var last = segments[^1];
            if (last.Start + last.Length == start && last.PatternIndex == patternIndex)
            {
                segments[^1] = last with { Length = last.Length + length };
                return;
            }
        }

        segments.Add(new HighlightSegment(start, length, patternIndex));
    }
}
