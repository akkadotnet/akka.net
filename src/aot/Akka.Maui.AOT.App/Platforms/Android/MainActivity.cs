//-----------------------------------------------------------------------
// <copyright file="MainActivity.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Akka.Maui.AOT.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Android has no command line: 'adb shell am start -n net.getakka.mauicanary/<activity> --ez selftest true'.
        // Read before base.OnCreate, which creates the window and the page.
        if (Intent?.GetBooleanExtra("selftest", false) == true)
            CanaryRun.EnableSelfTest();

        base.OnCreate(savedInstanceState);
    }
}
