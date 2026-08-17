using System.Threading.Tasks;
using Android.App;
using Android.Content.PM;
using Android.OS;
using ArctZ.ViewModels;
using Avalonia;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;

namespace ArctZ.Android
{
    [Activity(
        Label = "ArctZ",
        Theme = "@style/MyTheme.NoActionBar",
        Icon = "@drawable/icon",
        MainLauncher = true,
        // Тап по уведомлению фонового сеанса поднимает уже существующую активность, а не
        // вторую копию экрана поверх живой.
        LaunchMode = LaunchMode.SingleTask,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
    public class MainActivity : AvaloniaMainActivity
    {
        private const int BluetoothPermissionRequestCode = 5001;

        public static MainActivity? Instance { get; private set; }

        private TaskCompletionSource<bool>? _permissionRequestCompletion;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Instance = this;
        }

        /// <summary>
        /// Смахивание из недавних не убивает процесс: следующий запуск Android отдаёт тому же
        /// процессу, а значит стартовый путь Avalonia (App.OnFrameworkInitializationCompleted,
        /// откуда идёт единственный явный вызов автоподключения) второй раз не отрабатывает.
        /// Без этого вызова приложение после принудительного закрытия открывалось бы
        /// отключённым и ждало ручного «Подключить».
        /// </summary>
        protected override void OnResume()
        {
            base.OnResume();

            var connection = global::ArctZ.App.Services?.GetService<ConnectionViewModel>();
            _ = connection?.EnsureAutoConnectAsync();
        }

        protected override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            base.OnDestroy();
        }

        public Task<bool> RequestPermissionsAsync(string[] permissions)
        {
            _permissionRequestCompletion = new TaskCompletionSource<bool>();
            RequestPermissions(permissions, BluetoothPermissionRequestCode);
            return _permissionRequestCompletion.Task;
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            if (requestCode != BluetoothPermissionRequestCode)
            {
                return;
            }

            var granted = grantResults.Length > 0;
            foreach (var result in grantResults)
            {
                if (result != Permission.Granted)
                {
                    granted = false;
                }
            }

            _permissionRequestCompletion?.TrySetResult(granted);
            _permissionRequestCompletion = null;
        }
    }
}
