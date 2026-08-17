using ArctZ.ViewModels;
using Avalonia.Controls;

namespace ArctZ.Views
{
    public partial class MainWindow : Window
    {
        private readonly WindowCloseCoordinator _closeCoordinator;

        public MainWindow()
        {
            InitializeComponent();
            _closeCoordinator = new WindowCloseCoordinator(
                () => ViewModel is { } vm ? vm.ShutdownAsync() : System.Threading.Tasks.Task.FromResult(true),
                Close);
        }

        private ProgramViewModel? ViewModel => DataContext as ProgramViewModel;

        /// <summary>Крестик и Alt+F4 закрывают окно, минуя пункт меню «Выход», поэтому останов
        /// станка навешен и сюда — иначе всё, что уже ушло в буфер прошивки, продолжило бы
        /// исполняться после того, как приложения не стало.</summary>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);

            if (e.Cancel)
            {
                return;
            }

            e.Cancel = _closeCoordinator.ShouldCancelClose(ViewModel?.IsShutdownComplete ?? true);
        }
    }
}
