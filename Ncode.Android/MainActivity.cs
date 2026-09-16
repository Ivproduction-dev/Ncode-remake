// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using global::Android.Content.PM;
using global::Android.Views;
using Ncode.Core.Abstractions;
using Ncode.Platform;
using Ncode.Rendering;

namespace Ncode.Android;

[Activity(MainLauncher = true, Exported = true, Theme = "@style/MainTheme",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
#if SCREEN_PORTRAIT
    ScreenOrientation = ScreenOrientation.Portrait,
#elif SCREEN_LANDSCAPE
    ScreenOrientation = ScreenOrientation.Landscape,
#else
    ScreenOrientation = ScreenOrientation.Unspecified,
#endif
    LaunchMode = LaunchMode.SingleTask)]
public class MainActivity : Activity
{
    private AndroidGameView? _gameView;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        try { ActionBar?.Hide(); } catch { }
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        Window?.AddFlags(WindowManagerFlags.Fullscreen);
        Window?.DecorView.SystemUiVisibility = (StatusBarVisibility)(SystemUiFlags.HideNavigation | SystemUiFlags.ImmersiveSticky | SystemUiFlags.Fullscreen);

        global::Ncode.Platform.AndroidDialogService.CurrentActivity = this;

        _gameView = new AndroidGameView(this);
        SetContentView(_gameView);

        var prevHost = global::Ncode.Core.Abstractions.GameHostService.Current;
        var newHost = new global::Ncode.Rendering.AndroidGameHost(_gameView);
        newHost.OnKeyDown = prevHost.OnKeyDown;
        newHost.OnKeyUp = prevHost.OnKeyUp;
        newHost.OnPointerDown = prevHost.OnPointerDown;
        newHost.GetObjectsToRender = prevHost.GetObjectsToRender;
        global::Ncode.Core.Abstractions.GameHostService.Current = newHost;
        global::Ncode.Core.Abstractions.AudioService.Current = new Ncode.Audio.AndroidAudioPlayer();
        global::Ncode.Core.Abstractions.DialogService.Current = new AndroidDialogService();
        global::Ncode.Core.Abstractions.ClipboardService.Current = new AndroidClipboard();

        _ = Task.Run(() =>
        {
            try { Program.RunAndroid(this, (global::Ncode.Rendering.AndroidGameHost)global::Ncode.Core.Abstractions.GameHostService.Current); }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("Ncode", ex.ToString());
                try { _gameView?.SetError("Ошибка запуска игры:\n" + ex.Message); } catch { }
            }
        });
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        global::Ncode.Platform.AndroidDialogService.CurrentActivity = null;
    }
}
