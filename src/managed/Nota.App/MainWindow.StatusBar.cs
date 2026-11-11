// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Settings → Appearance → Show the status bar: the bar at the bottom of the main window can be
// hidden to give the views its height. Hidden, everything on it goes with it (messages,
// downloads, updates, dropouts, sample rate, version). The choice is saved, and the window
// follows every save, so a change in Settings applies live.

using Microsoft.Extensions.DependencyInjection;
using Nota.Application;

namespace Nota.App;

public partial class MainWindow
{
    // Once, from the constructor: the bar starts as the settings say and follows them.
    private void SetUpStatusBarVisibility()
    {
        var settings = App.Services?.GetService<ISettingsService>();
        if (settings is null) return;
        void Apply() => StatusBar.IsVisible = settings.Current.ShowStatusBar;
        Apply();
        settings.Changed += Apply;
        Closed += (_, _) => settings.Changed -= Apply;
    }
}
