using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
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
    public static readonly DependencyProperty SourceTextProperty =
        DependencyProperty.Register(nameof(SourceText), typeof(string), typeof(HighlightTextBlock),
            new PropertyMetadata("", OnHighlightPropertyChanged));

    public static readonly DependencyProperty RegexPatternProperty =
        DependencyProperty.Register(nameof(RegexPattern), typeof(string), typeof(HighlightTextBlock),
            new PropertyMetadata("", OnHighlightPropertyChanged));

    public static readonly DependencyProperty BaseForegroundProperty =
        DependencyProperty.Register(nameof(BaseForeground), typeof(Brush), typeof(HighlightTextBlock),
            new PropertyMetadata(Brushes.Black, OnHighlightPropertyChanged));

    private bool _isUpdating;

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
        if (d is HighlightTextBlock block) block.UpdateHighlight();
    }

    private void UpdateHighlight()
    {
        if (_isUpdating) return;
        _isUpdating = true;
        try
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
                var matches = Regex.Matches(text, pattern, RegexOptions.None, SerialPortTool.Helpers.RegexHelper.DefaultMatchTimeout);
                int last = 0;
                foreach (Match m in matches)
                {
                    if (m.Index > last)
                        Inlines.Add(new Run(text.Substring(last, m.Index - last)) { Foreground = baseFg });
                    Inlines.Add(new Run(m.Value)
                    {
                        Background = new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                        Foreground = Brushes.Black,
                        FontWeight = FontWeights.Bold
                    });
                    last = m.Index + m.Length;
                }
                if (last < text.Length)
                    Inlines.Add(new Run(text.Substring(last)) { Foreground = baseFg });
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
        finally
        {
            _isUpdating = false;
        }
    }
}
