// -----------------------------------------------------------------------
//  <copyright file="Util.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Akka.Configuration;
using Akka.Configuration.Hocon;
using Akka.Util.Internal;

namespace Akka.Hosting
{
    public static class Util
    {
        // HACK: MAUI runtime detection
        private static bool? _runningInMaui;
        internal static bool IsRunningInMaui
        {
            get
            {
                _runningInMaui ??= DetectMaui(AppDomain.CurrentDomain.GetAssemblies());
                return _runningInMaui.Value;
            }
        }

        internal static bool DetectMaui(IEnumerable<Assembly> assemblies)
        {
            try
            {
                return assemblies.Any(IsMauiAssembly);
            }
            catch
            {
                // detection is best effort - failing to detect means "not MAUI"
                return false;
            }
        }

        internal static bool IsMauiAssembly(Assembly? assembly)
        {
            try
            {
                // Don't use Assembly.GetName() here: it builds a CultureInfo for satellite assemblies
                // (e.g. "cs" from Microsoft.Data.SqlClient) and throws CultureNotFoundException
                // when InvariantGlobalization is enabled. FullName is a plain string that starts
                // with the simple assembly name.
                return IsMauiAssemblyName(assembly?.FullName);
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsMauiAssemblyName(string? assemblyFullName)
            => assemblyFullName?.StartsWith("Microsoft.Maui", StringComparison.Ordinal) ?? false;

        public static Config MoveTo(this Config config, string path)
        {
            var rootObj = new HoconObject();
            var rootValue = new HoconValue();
            rootValue.Values.Add(rootObj);
            
            var lastObject = rootObj;

            var keys = path.SplitDottedPathHonouringQuotes().ToArray();
            for (var i = 0; i < keys.Length - 1; i++)
            {
                var key = keys[i];
                var innerObject = new HoconObject();
                var innerValue = new HoconValue();
                innerValue.Values.Add(innerObject);
                
                lastObject.GetOrCreateKey(key);
                lastObject.Items[key] = innerValue;
                lastObject = innerObject;
            }
            lastObject.Items[keys[keys.Length - 1]] = config.Root;
            
            return new Config(new HoconRoot(rootValue));
        }
    }
}