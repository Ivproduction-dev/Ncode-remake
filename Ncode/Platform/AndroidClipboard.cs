using System;
using Ncode.Core.Abstractions;

namespace Ncode.Platform;

public sealed class AndroidClipboard : IClipboardService
{
#if ANDROID
    public void Copy(string text)
    {
        try
        {
            var cm = (Android.Content.ClipboardManager?)Android.App.Application.Context.GetSystemService(Android.Content.Context.ClipboardService);
            if (cm != null)
            {
                var clip = Android.Content.ClipData.NewPlainText("ncode", text);
                cm.PrimaryClip = clip;
            }
        }
        catch { }
    }

    public string Paste()
    {
        try
        {
            var cm = (Android.Content.ClipboardManager?)Android.App.Application.Context.GetSystemService(Android.Content.Context.ClipboardService);
            if (cm != null && cm.HasPrimaryClip && cm.PrimaryClip != null && cm.PrimaryClip.ItemCount > 0)
            {
                var item = cm.PrimaryClip.GetItemAt(0);
                return item?.Text ?? "";
            }
        }
        catch { }
        return "";
    }
#else
    private string _buf = "";
    public void Copy(string text) => _buf = text;
    public string Paste() => _buf;
#endif
}
