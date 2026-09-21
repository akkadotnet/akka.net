// -----------------------------------------------------------------------
//  <copyright file="Util.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
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
                _runningInMaui ??= AppDomain.CurrentDomain.GetAssemblies().Any(asm => asm?.GetName()?.Name?.StartsWith("Microsoft.Maui") ?? false);
                return _runningInMaui.Value;
            }
        }

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