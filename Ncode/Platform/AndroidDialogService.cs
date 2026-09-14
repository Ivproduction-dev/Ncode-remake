using System;
using System.Threading;
using Ncode.Core.Abstractions;

namespace Ncode.Platform;

public sealed class AndroidDialogService : IDialogService
{
#if ANDROID
    public static Android.App.Activity? CurrentActivity { get; set; }

    public void ShowMessage(string text)
    {
        var activity = CurrentActivity;
        var ctx = activity ?? (Android.Content.Context?)Android.App.Application.Context;
        if (ctx == null) return;
        if (activity != null)
        {
            activity.RunOnUiThread(() =>
            {
                try { Android.Widget.Toast.MakeText(ctx, text, Android.Widget.ToastLength.Long)?.Show(); }
                catch { }
            });
        }
        else
        {
            try { Android.Widget.Toast.MakeText(ctx, text, Android.Widget.ToastLength.Long)?.Show(); }
            catch { }
        }
    }

    public void ShowError(string text)
    {
        var activity = CurrentActivity;
        var ctx = activity ?? (Android.Content.Context?)Android.App.Application.Context;
        if (ctx == null) return;
        var msg = "Ошибка: " + text;
        if (activity != null)
        {
            activity.RunOnUiThread(() =>
            {
                try { Android.Widget.Toast.MakeText(ctx, msg, Android.Widget.ToastLength.Long)?.Show(); }
                catch { }
            });
        }
        else
        {
            try { Android.Widget.Toast.MakeText(ctx, msg, Android.Widget.ToastLength.Long)?.Show(); }
            catch { }
        }
    }

    public bool AskYesNo(string text)
    {
        var activity = CurrentActivity;
        if (activity != null)
        {
            bool result = false;
            using var done = new ManualResetEventSlim(false);
            activity.RunOnUiThread(() =>
            {
                try
                {
                    new Android.App.AlertDialog.Builder(activity)
                        .SetTitle("Ncode")
                        .SetMessage(text)
                        .SetPositiveButton("Да", (s, e) => { result = true; done.Set(); })
                        .SetNegativeButton("Нет", (s, e) => { result = false; done.Set(); })
                        .SetCancelable(false)
                        .Show();
                }
                catch
                {
                    done.Set();
                }
            });
            done.Wait();
            return result;
        }

        Console.Write("[да или нет] " + text + " (да/нет): ");
        var r = Console.ReadLine()?.Trim().ToLowerInvariant();
        return r is "да" or "y" or "yes" or "д";
    }

    private class DismissListener : Java.Lang.Object, Android.Content.IDialogInterfaceOnDismissListener
    {
        private readonly Action _onDismiss;
        public DismissListener(Action onDismiss) => _onDismiss = onDismiss;
        public void OnDismiss(Android.Content.IDialogInterface? dialog) => _onDismiss();
    }
#else
    public void ShowMessage(string text) { }
    public void ShowError(string text) { }
    public bool AskYesNo(string text) => false;
#endif
}
