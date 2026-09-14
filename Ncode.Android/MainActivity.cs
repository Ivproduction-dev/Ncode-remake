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

[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize, ScreenOrientation = ScreenOrientation.Unspecified)]
public class MainActivity : Activity
{
    private AndroidGameView? _gameView;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        Window?.AddFlags(WindowManagerFlags.Fullscreen);
        Window?.DecorView.SystemUiVisibility = (StatusBarVisibility)(SystemUiFlags.HideNavigation | SystemUiFlags.ImmersiveSticky | SystemUiFlags.Fullscreen);

        global::Ncode.Platform.AndroidDialogService.CurrentActivity = this;

        _gameView = new AndroidGameView(this);
        SetContentView(_gameView);

        global::Ncode.Core.Abstractions.GameHostService.Current = new global::Ncode.Rendering.AndroidGameHost(_gameView);
        global::Ncode.Core.Abstractions.AudioService.Current = new Ncode.Audio.AndroidAudioPlayer();
        global::Ncode.Core.Abstractions.DialogService.Current = new AndroidDialogService();
        global::Ncode.Core.Abstractions.ClipboardService.Current = new AndroidClipboard();

        _ = Task.Run(() =>
        {
            try { Program.RunAndroid(this, (global::Ncode.Rendering.AndroidGameHost)global::Ncode.Core.Abstractions.GameHostService.Current); }
            catch (Exception ex) { global::Android.Util.Log.Error("Ncode", ex.ToString()); }
        });
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        global::Ncode.Platform.AndroidDialogService.CurrentActivity = null;
    }
}
