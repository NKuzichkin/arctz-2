using System.Threading.Tasks;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;

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
