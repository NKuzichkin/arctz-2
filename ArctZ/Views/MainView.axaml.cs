using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ArctZ.Components.VirtualJoystick;
using ArctZ.Services.Program;
using ArctZ.ViewModels;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace ArctZ.Views
{
    public partial class MainView : UserControl
    {
        // Border(reveal-3).Margin(12→12+12=24 гор.) + BorderThickness(1+1=2) + ContentGrid.Margin(20+20=40)
        private const double ContentGridChromeWidth = 66;
        private const double MinRadius = 50;
        private const double MaxRadius = 110;
        private const double CenterGap = 24;
        private const double NarrowLayoutWidthThreshold = 700;

        // Border(reveal-3).Margin(12→12+12=24 верт.) + BorderThickness(1+1=2)
        private const double ContentBorderVerticalChrome = 26;
        // ContentGrid.Margin(20+20=40 верт.)
        private const double ContentGridVerticalMargin = 40;
        private const double JoystickBarTopMargin = 12;
        private const double ProgramPanelMinHeight = 160;
        // Подпись под джойстиком: StackPanel Spacing=4 + до 2 строк текста FontSize=12
        private const double JoystickLabelReservedHeight = 36;

        // Фолбэк для HeaderBorder.Bounds.Height на первом кадре, до первого layout-прохода
        // (однострочная шапка: Padding="12,10" + одна строка контента).
        private const double HeaderFallbackHeight = 44;

        public MainView()
        {
            InitializeComponent();
            SizeChanged += OnLayoutSizeChanged;
            HeaderBorder.SizeChanged += OnLayoutSizeChanged;   // header can re-wrap without resizing MainView
            DataContextChanged += OnDataContextChanged;
        }

        private ProgramViewModel? ViewModel => DataContext as ProgramViewModel;

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (DataContext is ProgramViewModel vm)
            {
                vm.Connection.SentGCodeLines.CollectionChanged -= OnSentGCodeLinesChanged;
                vm.Connection.SentGCodeLines.CollectionChanged += OnSentGCodeLinesChanged;
            }
        }

        private void OnSentGCodeLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add &&
                GCodeLogList.IsEffectivelyVisible &&
                sender is ObservableCollection<string> { Count: > 0 } lines)
            {
                // ScrollIntoView forces its own synchronous LayoutManager.ExecuteLayoutPass()
                // internally (Avalonia's VirtualizingStackPanel implementation). Calling it
                // inline here — mid CollectionChanged, right after AppendSentGCodeLine's
                // trim-then-add pair (RemoveAt(0) then Add once the 200-line cap is hit) — can
                // catch the panel's realized-element bookkeeping before it's settled from the
                // preceding Remove, handing that forced pass a transient rect with a NaN/negative
                // component. Layoutable.Arrange rejects that with "Invalid Arrange rectangle"
                // (InvalidOperationException) — reproduced by
                // ArctZ.Tests.Screenshots.GCodeLogAutoScrollTests. Posting instead of calling
                // inline lets the pending layout invalidation resolve normally first, so
                // ScrollIntoView's own pass sees settled state. Re-check Count/visibility at
                // execution time since more lines may have arrived (or the panel closed) by then.
                Dispatcher.UIThread.Post(() =>
                {
                    if (GCodeLogList.IsEffectivelyVisible && lines.Count > 0)
                    {
                        GCodeLogList.ScrollIntoView(lines.Count - 1);
                    }
                }, DispatcherPriority.Loaded);
            }
        }

        private void OnLayoutSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateJoystickRadius();

        private void UpdateJoystickRadius()
        {
            var radius = ComputeJoystickRadius(Bounds.Width, Bounds.Height, HeaderBorder.Bounds.Height);
            LeftJoystick.Radius = radius;
            RightJoystick.Radius = radius;

            if (ViewModel is { } vm)
            {
                vm.IsNarrowJoystickLayout = ComputeIsNarrowLayout(Bounds.Width);
            }
        }

        internal static double ComputeJoystickRadius(double mainViewWidth, double mainViewHeight, double headerHeight)
        {
            var effectiveHeaderHeight = headerHeight > 0 ? headerHeight : HeaderFallbackHeight;

            var contentGridWidth = mainViewWidth - ContentGridChromeWidth;
            var widthRadius = (contentGridWidth - CenterGap) / 4;

            var contentGridHeight = mainViewHeight - effectiveHeaderHeight - ContentBorderVerticalChrome
                - ContentGridVerticalMargin - JoystickBarTopMargin;
            var joystickRowBudget = contentGridHeight - ProgramPanelMinHeight;
            var heightRadius = (joystickRowBudget - JoystickLabelReservedHeight) / 2;

            return Math.Clamp(Math.Min(widthRadius, heightRadius), MinRadius, MaxRadius);
        }

        internal static bool ComputeIsNarrowLayout(double mainViewWidth) => mainViewWidth < NarrowLayoutWidthThreshold;

        private void OnLeftJoystickDown(object? sender, JoystickEventArgs e) => ViewModel?.OnLeftJoystickDown(e);

        private void OnLeftJoystickMove(object? sender, JoystickEventArgs e) => ViewModel?.OnLeftJoystickMove(e);

        private void OnLeftJoystickUp(object? sender, JoystickEventArgs e) => ViewModel?.OnLeftJoystickUp(e);

        private void OnRightJoystickDown(object? sender, JoystickEventArgs e) => ViewModel?.OnRightJoystickDown(e);

        private void OnRightJoystickMove(object? sender, JoystickEventArgs e) => ViewModel?.OnRightJoystickMove(e);

        private void OnRightJoystickUp(object? sender, JoystickEventArgs e) => ViewModel?.OnRightJoystickUp(e);

        /// <summary>
        /// Copying lives here rather than in the view model: the clipboard is reached through
        /// the hosting TopLevel, which a view model has no handle on. The report text itself is
        /// built and tested in AboutViewModel — this only moves it.
        /// </summary>
        private async void OnCopyDiagnosticsClick(object? sender, RoutedEventArgs e)
        {
            if (ViewModel?.About is not { } about)
            {
                return;
            }

            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            {
                return;
            }

            await clipboard.SetTextAsync(about.ReportText);
            about.MarkCopied();
        }

        private async void OnLibrarySelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ViewModel is { } vm && sender is ListBox { SelectedItem: ProgramLibraryItem summary })
            {
                await vm.LoadProgramCommand.ExecuteAsync(summary);
            }
        }
    }
}
