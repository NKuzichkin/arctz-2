using System.Threading.Tasks;
using Android.Content.PM;

namespace ArctZ.Android;

/// <summary>
/// Тонкая обёртка над CheckSelfPermission/RequestPermissions без AndroidX —
/// в csproj нет AndroidX.Core, а обе нужные операции есть в базовом Context/Activity.
/// </summary>
public sealed class AndroidPermissions
{
    public Task<bool> RequestAsync(string[] permissions)
    {
        var context = global::Android.App.Application.Context;
        var missing = System.Array.FindAll(permissions, p => context.CheckSelfPermission(p) != Permission.Granted);

        if (missing.Length == 0)
        {
            return Task.FromResult(true);
        }

        var activity = MainActivity.Instance;
        return activity is null
            ? Task.FromResult(false)
            : activity.RequestPermissionsAsync(missing);
    }
}
