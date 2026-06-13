using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SerialPortTool.Models;
using SerialPortTool.ViewModels;

namespace SerialPortTool.Views;

public class RawLogViewer : RichTextBox
{
    private const double MinPageWidth = 800;

    private static readonly Brush TxBrush = CreateFrozenBrush(46, 125, 50);
    private static readonly Brush RxBrush = CreateFrozenBrush(21, 101, 192);

    private ObservableCollection<SerialDataModel>? _allData;
    private double _maxContentWidth = MinPageWidth;
    private MenuItem? _copyMenuItem;
    private string? _snapshotCopyText;
    private bool _isContextMenuOpen;
    private bool _needsRebuildAfterMenu;
    private bool _syncQueued;

    public RawLogViewer()
    {
        IsReadOnly = true;
        IsDocumentEnabled = true;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        FontFamily = new FontFamily("Consolas, 'Courier New', monospace");
        FontSize = 12;
        BorderThickness = new Thickness(0);
        Background = Brushes.White;
        Document.PagePadding = new Thickness(4);
        Document.PageWidth = MinPageWidth;

        _copyMenuItem = new MenuItem { Header = "复制" };
        _copyMenuItem.Click += OnCopyMenuClick;

        ContextMenu = new ContextMenu();
        ContextMenu.Items.Add(_copyMenuItem);
        ContextMenu.SetValue(Popup.PopupAnimationProperty, PopupAnimation.None);
        ContextMenu.Opened += OnContextMenuOpened;
        ContextMenu.Closed += OnContextMenuClosed;

        AddHandler(ContextMenuOpeningEvent, new ContextMenuEventHandler(OnContextMenuOpening), true);
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, CopyExecuted, CopyCanExecute));

        Loaded += (_, _) => AttachToViewModel();
        DataContextChanged += (_, _) => AttachToViewModel();
        Unloaded += (_, _) => DetachCollection();
    }

    public void ScrollToLatest()
    {
        ScrollToEnd();
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        _snapshotCopyText = CaptureSelectedText();
        if (_copyMenuItem != null)
            _copyMenuItem.IsEnabled = !string.IsNullOrEmpty(_snapshotCopyText);
    }

    private void OnContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        _isContextMenuOpen = true;
        if (_copyMenuItem != null)
            _copyMenuItem.IsEnabled = !string.IsNullOrEmpty(_snapshotCopyText);
    }

    private void OnContextMenuClosed(object? sender, RoutedEventArgs e)
    {
        _isContextMenuOpen = false;
        _snapshotCopyText = null;

        if (!_needsRebuildAfterMenu)
            return;

        _needsRebuildAfterMenu = false;
        Dispatcher.BeginInvoke(() => QueueDocumentSync(force: true), DispatcherPriority.ApplicationIdle);
    }

    private void OnCopyMenuClick(object sender, RoutedEventArgs e)
    {
        var captured = _snapshotCopyText;
        if (string.IsNullOrEmpty(captured))
            return;

        if (ContextMenu != null)
            ContextMenu.IsOpen = false;

        WriteClipboardAsync(captured);
    }

    private void CopyCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !string.IsNullOrEmpty(_snapshotCopyText) || !Selection.IsEmpty;
        e.Handled = true;
    }

    private void CopyExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        _snapshotCopyText ??= CaptureSelectedText();
        var text = _snapshotCopyText;
        if (!string.IsNullOrEmpty(text))
            WriteClipboardAsync(text);
        e.Handled = true;
    }

    private static void WriteClipboardAsync(string text)
    {
        var thread = new Thread(() => WriteClipboard(text))
        {
            IsBackground = true,
            Name = "RawLogClipboard"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void WriteClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
            }
            catch
            {
                // 忽略剪贴板被占用等异常
            }
        }
    }

    private string? CaptureSelectedText()
    {
        try
        {
            if (Selection.IsEmpty)
                return null;

            return TryCaptureFromDataSource() ?? new TextRange(Selection.Start, Selection.End).Text;
        }
        catch
        {
            return null;
        }
    }

    private string? TryCaptureFromDataSource()
    {
        if (_allData == null || _allData.Count == 0 || Document.Blocks.Count == 0)
            return null;

        var startParagraph = GetContainingParagraph(Selection.Start);
        var endParagraph = GetContainingParagraph(Selection.End);
        if (startParagraph == null || endParagraph == null)
            return null;

        int startIndex = GetParagraphIndex(startParagraph);
        int endIndex = GetParagraphIndex(endParagraph);
        if (startIndex < 0 || endIndex < 0)
            return null;

        if (startIndex > endIndex)
            (startIndex, endIndex) = (endIndex, startIndex);

        if (endIndex >= _allData.Count)
            endIndex = _allData.Count - 1;

        if (startIndex == endIndex)
            return new TextRange(Selection.Start, Selection.End).Text;

        if (IsAtParagraphStart(Selection.Start, startParagraph) && IsAtParagraphEnd(Selection.End, endParagraph))
        {
            var fullLineBuilder = new StringBuilder((endIndex - startIndex + 1) * 64);
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (i > startIndex)
                    fullLineBuilder.AppendLine();
                fullLineBuilder.Append(_allData[i].DisplayLine);
            }

            return fullLineBuilder.ToString();
        }

        var builder = new StringBuilder((endIndex - startIndex + 1) * 64);
        AppendSelectionSegment(builder, Selection.Start, startParagraph.ContentEnd, startParagraph, startIndex, isFirstSegment: true);

        for (int i = startIndex + 1; i < endIndex; i++)
        {
            builder.AppendLine();
            builder.Append(_allData[i].DisplayLine);
        }

        AppendSelectionSegment(builder, endParagraph.ContentStart, Selection.End, endParagraph, endIndex, isFirstSegment: false);
        return builder.ToString();
    }

    private void AppendSelectionSegment(
        StringBuilder builder,
        TextPointer segmentStart,
        TextPointer segmentEnd,
        Paragraph paragraph,
        int lineIndex,
        bool isFirstSegment)
    {
        if (lineIndex < 0 || lineIndex >= _allData.Count)
            return;

        if (!isFirstSegment)
            builder.AppendLine();

        if (segmentStart.CompareTo(segmentEnd) >= 0)
            return;

        if (IsAtParagraphStart(segmentStart, paragraph) && IsAtParagraphEnd(segmentEnd, paragraph))
        {
            builder.Append(_allData[lineIndex].DisplayLine);
            return;
        }

        builder.Append(new TextRange(segmentStart, segmentEnd).Text);
    }

    private static bool IsAtParagraphStart(TextPointer position, Paragraph paragraph) =>
        position.CompareTo(paragraph.ContentStart) <= 0;

    private static bool IsAtParagraphEnd(TextPointer position, Paragraph paragraph) =>
        position.CompareTo(paragraph.ContentEnd) >= 0;

    private static Paragraph? GetContainingParagraph(TextPointer pointer)
    {
        if (pointer.Parent is not TextElement element)
            return null;

        var current = element;
        while (current is not Paragraph && current.Parent is TextElement parent)
            current = parent;

        return current as Paragraph;
    }

    private int GetParagraphIndex(Paragraph paragraph)
    {
        int index = 0;
        foreach (Block block in Document.Blocks)
        {
            if (block == paragraph)
                return index;
            index++;
        }

        return -1;
    }

    private void AttachToViewModel()
    {
        if (DataContext is not SerialPortViewModel vm)
        {
            DetachCollection();
            return;
        }

        if (ReferenceEquals(_allData, vm.AllData))
            return;

        DetachCollection();
        _allData = vm.AllData;
        _allData.CollectionChanged += OnAllDataChanged;
        RebuildDocument();
    }

    private void DetachCollection()
    {
        if (_allData == null)
            return;

        _allData.CollectionChanged -= OnAllDataChanged;
        _allData = null;
    }

    private void OnAllDataChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isContextMenuOpen)
        {
            _needsRebuildAfterMenu = true;
            return;
        }

        QueueDocumentSync(force: false);
    }

    private void QueueDocumentSync(bool force)
    {
        if (_isContextMenuOpen)
        {
            _needsRebuildAfterMenu = true;
            return;
        }

        if (!force && _syncQueued)
            return;

        _syncQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _syncQueued = false;
            if (_isContextMenuOpen)
            {
                _needsRebuildAfterMenu = true;
                return;
            }

            SyncDocumentFromCollection();
        }, DispatcherPriority.Background);
    }

    private void SyncDocumentFromCollection()
    {
        if (_allData == null)
        {
            ClearDocument();
            return;
        }

        var targetCount = _allData.Count;
        var currentCount = Document.Blocks.Count;

        if (targetCount == 0)
        {
            ClearDocument();
            return;
        }

        if (currentCount == 0 || targetCount < currentCount || IsDocumentContentStale())
        {
            RebuildDocument();
            return;
        }

        for (int i = currentCount; i < targetCount; i++)
            AppendLine(_allData[i]);
    }

    private bool IsDocumentContentStale()
    {
        if (_allData == null || _allData.Count == 0 || Document.Blocks.FirstBlock is not Paragraph firstParagraph)
            return _allData != null && _allData.Count > 0;

        var firstLine = new TextRange(firstParagraph.ContentStart, firstParagraph.ContentEnd).Text.TrimEnd('\r', '\n');
        return !string.Equals(firstLine, _allData[0].DisplayLine, StringComparison.Ordinal);
    }

    private void RebuildDocument()
    {
        ClearDocument();
        if (_allData == null)
            return;

        foreach (var item in _allData)
            AppendLine(item);
    }

    private void ClearDocument()
    {
        CollapseSelectionBeforeMutation();
        Document.Blocks.Clear();
        ResetPageWidth();
    }

    private void CollapseSelectionBeforeMutation()
    {
        try
        {
            if (!Selection.IsEmpty)
                Selection.Select(Document.ContentEnd, Document.ContentEnd);
        }
        catch
        {
            // 选区已失效时忽略
        }
    }

    private void AppendLine(SerialDataModel item)
    {
        var line = item.DisplayLine;
        var paragraph = new Paragraph(new Run(line) { Foreground = GetDirectionBrush(item.Direction) })
        {
            Margin = new Thickness(0)
        };
        Document.Blocks.Add(paragraph);
        UpdatePageWidth(line);
    }

    private void ResetPageWidth()
    {
        _maxContentWidth = Math.Max(MinPageWidth, ActualWidth);
        Document.PageWidth = _maxContentWidth;
    }

    private void UpdatePageWidth(string line)
    {
        var width = MeasureLineWidth(line) + Document.PagePadding.Left + Document.PagePadding.Right;
        if (width <= _maxContentWidth)
            return;

        _maxContentWidth = width;
        Document.PageWidth = _maxContentWidth;
    }

    private double MeasureLineWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var dpi = VisualTreeHelper.GetDpi(this);
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection,
            typeface,
            FontSize,
            Brushes.Black,
            dpi.PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    private static Brush GetDirectionBrush(string direction) =>
        direction == "TX" ? TxBrush : RxBrush;

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
