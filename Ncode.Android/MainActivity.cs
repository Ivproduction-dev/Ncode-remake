// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using Android.Content.PM;
using Android.Views;
using Ncode.Core.Abstractions;
using Ncode.Platform;
using Ncode.Rendering;

namespace Ncode.Android;

[Activity(Label = "@string/app_name", MainLauncher = true, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize, ScreenOrientation = ScreenOrientation.Unspecified)]
public class MainActivity : Activity
{
    private AndroidGameView? _gameView;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        Window?.AddFlags(WindowManagerFlags.Fullscreen);
        Window?.DecorView.SystemUiVisibility = (StatusBarVisibility)(SystemUiFlags.HideNavigation | SystemUiFlags.ImmersiveSticky | SystemUiFlags.Fullscreen);

        AndroidDialogService.CurrentActivity = this;

        _gameView = new AndroidGameView(this);
        SetContentView(_gameView);

        GameHostService.Current = new AndroidGameHost(_gameView);
        AudioService.Current = new Ncode.Audio.AndroidAudioPlayer();
        DialogService.Current = new AndroidDialogService();
        ClipboardService.Current = new AndroidClipboard();

        _ = Task.Run(() =>
        {
            try { Program.RunAndroid(this, (AndroidGameHost)GameHostService.Current); }
            catch (Exception ex) { global::Android.Util.Log.Error("Ncode", ex.ToString()); }
        });
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        AndroidDialogService.CurrentActivity = null;
    }
}
