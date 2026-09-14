using System;
using System.Threading;
#if !ANDROID
using System.Windows.Forms;
#endif
using Ncode.Core.Abstractions;

namespace Ncode.Platform;

public sealed class WindowsClipboard : IClipboardService
{
#if !ANDROID
    public void Copy(string text)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            Clipboard.SetText(text);
            return;
        }

        var thread = new Thread(() =>
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch { }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    public string Paste()
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return Clipboard.GetText();
        }

        string result = "";
        var thread = new Thread(() =>
        {
            try
            {
                result = Clipboard.GetText();
            }
            catch { }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }
#else
    private string _buf = "";
    public void Copy(string text) => _buf = text;
    public string Paste() => _buf;
#endif
}
