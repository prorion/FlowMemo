using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

using FlowMemo.Core.Sync;
using FlowMemo.WpfUi.Models;
using FlowMemo.WpfUi.Resources;

namespace FlowMemo.WpfUi.Views;

/// <summary>
/// 이 PC 내용과 다른 PC 내용을 나란히 보여 주고, 어떻게 할지 고른다.
/// 양쪽에서 고쳤을 때는 합친 결과를 함께 보여 준다.
/// </summary>
public partial class SyncDiffWindow : Window
{
    // 반투명이라 밝은 테마와 어두운 테마에 모두 어울린다.
    private static readonly Brush RemovedBrush = Frozen(0x40, 0xE5, 0x53, 0x4B);
    private static readonly Brush AddedBrush = Frozen(0x40, 0x2E, 0xA0, 0x43);
    private static readonly Brush RemovedStrongBrush = Frozen(0x90, 0xE5, 0x53, 0x4B);
    private static readonly Brush AddedStrongBrush = Frozen(0x90, 0x2E, 0xA0, 0x43);
    private static readonly Brush EmptyBrush = Frozen(0x14, 0x80, 0x80, 0x80);

    public SyncDiffWindow(NoteSyncRow row, string localContent)
    {
        InitializeComponent();

        string device = row.PendingDevice ?? "";
        bool conflict = row.Status == SyncNoteStatus.Conflict;

        RemoteHeaderText.Text = device;
        IntroText.Text = string.Format(conflict ? Strings.SyncDiff_ConflictIntro : Strings.SyncDiff_IncomingIntro, device);

        foreach (DiffRow diffRow in TextDiff.Compute(localContent, row.PendingContent))
            DiffRowsPanel.Children.Add(BuildRow(diffRow));

        if (conflict)
            ShowConflictMode(row, localContent, device);
        else
        {
            AcceptButton.Visibility = Visibility.Visible;
            SkipButton.Visibility = Visibility.Visible;
        }
    }

    public SyncDecision Decision { get; private set; } = SyncDecision.None;

    public string? MergedText { get; private set; }

    private void ShowConflictMode(NoteSyncRow row, string localContent, string device)
    {
        MergeResult merge = ThreeWayMerge.Merge(row.BaseContent, localContent, row.PendingContent, Strings.SyncDiff_LocalHeader, device);

        MergePanel.Visibility = Visibility.Visible;
        MergeStatusText.Text = merge.ConflictCount == 0
            ? Strings.SyncDiff_MergeClean
            : string.Format(Strings.SyncDiff_MergeConflicts, merge.ConflictCount);
        MergeTextBox.Text = merge.Text;

        SaveMergedButton.Visibility = Visibility.Visible;
        KeepLocalButton.Visibility = Visibility.Visible;
        TakeRemoteButton.Visibility = Visibility.Visible;
        KeepBothButton.Visibility = Visibility.Visible;
    }

    private static Grid BuildRow(DiffRow row)
    {
        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        bool changed = row.Kind != DiffKind.Same;

        AddCell(grid, 0, row.LeftNumber, row.Left, row.Right, changed ? RemovedBrush : null, RemovedStrongBrush, row.Kind == DiffKind.Modified);
        AddCell(grid, 2, row.RightNumber, row.Right, row.Left, changed ? AddedBrush : null, AddedStrongBrush, row.Kind == DiffKind.Modified);

        return grid;
    }

    private static void AddCell(Grid grid, int column, int? number, string? text, string? otherText, Brush? background, Brush strongBrush, bool highlightChange)
    {
        Brush? cellBackground = text is null ? EmptyBrush : background;

        Border numberCell = new()
        {
            Background = cellBackground,
            Child = new TextBlock
            {
                Text = number?.ToString() ?? "",
                FontSize = 11,
                Opacity = 0.6,
                TextAlignment = TextAlignment.Right,
                Padding = new Thickness(0, 2, 6, 2)
            }
        };
        Grid.SetColumn(numberCell, column);
        grid.Children.Add(numberCell);

        TextBlock content = new()
        {
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(0, 2, 4, 2)
        };

        if (highlightChange && text is not null && otherText is not null)
        {
            // 바뀐 줄 안에서 실제로 달라진 글자만 진하게 표시한다.
            (int prefix, int suffix) = TextDiff.ChangedSpan(text, otherText);
            content.Inlines.Add(new Run(text[..prefix]));
            content.Inlines.Add(new Run(text[prefix..(text.Length - suffix)]) { Background = strongBrush });
            content.Inlines.Add(new Run(text[(text.Length - suffix)..]));
        }
        else
        {
            content.Text = text ?? "";
        }

        Border contentCell = new() { Background = cellBackground, Child = content };
        Grid.SetColumn(contentCell, column + 1);
        grid.Children.Add(contentCell);
    }

    private static SolidColorBrush Frozen(byte a, byte r, byte g, byte b)
    {
        SolidColorBrush brush = new(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private void MergeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        => SaveMergedButton.IsEnabled = !ThreeWayMerge.HasConflictMarkers(MergeTextBox.Text);

    private void Finish(SyncDecision decision)
    {
        Decision = decision;
        Close();
    }

    private void SaveMergedButton_Click(object sender, RoutedEventArgs e)
    {
        MergedText = MergeTextBox.Text;
        Finish(SyncDecision.Merge);
    }

    private void KeepLocalButton_Click(object sender, RoutedEventArgs e)
        => Finish(SyncDecision.KeepLocal);

    private void TakeRemoteButton_Click(object sender, RoutedEventArgs e)
        => Finish(SyncDecision.TakeRemote);

    private void KeepBothButton_Click(object sender, RoutedEventArgs e)
        => Finish(SyncDecision.KeepBoth);

    private void SkipButton_Click(object sender, RoutedEventArgs e)
        => Finish(SyncDecision.Skip);

    private void LaterButton_Click(object sender, RoutedEventArgs e)
        => Finish(SyncDecision.None);
}
