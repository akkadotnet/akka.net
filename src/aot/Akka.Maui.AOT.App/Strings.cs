//-----------------------------------------------------------------------
// <copyright file="Strings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Reflection;
using System.Resources;

namespace Akka.Maui.AOT.App;

/// <summary>
/// Reads Resources/Strings.resx. Resources/Strings.cs.resx makes the build emit a 'cs' satellite assembly, which is
/// the other half of the Akka.Hosting #8782 setup (satellite assemblies + InvariantGlobalization).
/// </summary>
internal static class Strings
{
    public const string SatelliteAssemblyName = "Akka.Maui.AOT.App.resources, Culture=cs";

    private static readonly ResourceManager Resources = new("Akka.Maui.AOT.App.Resources.Strings", typeof(Strings).Assembly);

    public static string Title => Get(nameof(Title));

    public static string Subtitle => Get(nameof(Subtitle));

    private static string Get(string name) =>
        Resources.GetString(name) ?? throw new InvalidOperationException($"resource '{name}' is missing");

    /// <summary>
    /// Tries to load the 'cs' satellite into the process the way a localized lookup would, before Akka.Hosting's MAUI
    /// detection walks the loaded assemblies. Returns what happened, for the log.
    /// </summary>
    public static string TryLoadSatellite()
    {
        try
        {
            var satellite = Assembly.Load(new AssemblyName(SatelliteAssemblyName));
            return $"loaded {satellite.FullName}";
        }
        catch (Exception ex)
        {
            // not every runtime can load a satellite by name under invariant globalization; that is fine, the
            // detection must not crash either way
            return $"not loaded ({ex.GetType().Name}: {ex.Message})";
        }
    }
}
