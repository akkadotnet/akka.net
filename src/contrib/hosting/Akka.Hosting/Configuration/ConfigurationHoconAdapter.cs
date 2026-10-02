// -----------------------------------------------------------------------
//  <copyright file="IConfigurationAdapter.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Akka.Configuration;
using Akka.Configuration.Hocon;
using Akka.Util.Internal;
using Microsoft.Extensions.Configuration;

namespace Akka.Hosting.Configuration
{
    public static class ConfigurationHoconAdapter
    {
        public static Config ToHocon(this IConfiguration config, bool normalizeKeys = true)
        {
            var rootObject = new HoconObject();
            if (config is IConfigurationSection section)
            {
                var value = section.ExpandKey(rootObject, normalizeKeys);
                value.AppendValue(section.ToHoconElement(normalizeKeys));
            }
            else
            {
                foreach (var child in config.GetChildren())
                {
                    var value = child.ExpandKey(rootObject, normalizeKeys);
                    value.AppendValue(child.ToHoconElement(normalizeKeys));
                }
            }
            
            var rootValue = new HoconValue();
            rootValue.AppendValue(rootObject);
            return new Config(new HoconRoot(rootValue));
        }

        private static HoconValue ExpandKey(this IConfigurationSection config, HoconObject parent, bool normalizeKeys)
        {
            // Sanitize configuration brought in from environment variables, 
            // "__" are already converted to ":" by the environment configuration provider.
            var sanitized = (normalizeKeys ? config.Key.ToLowerInvariant() : config.Key).Replace("_", "-");
            var keys = sanitized.SplitDottedPathHonouringQuotes().ToList();

            // HOCON does not support objects with empty key name.
            // The most probable source for this is a double underscore prefixed environment variable, which
            // confuses MS.EXT.Configuration.EnvironmentVariables. Just create a temporary name for it.
            if(keys.Count == 0)
                return parent.GetOrCreateKey("__");
            
            // No need to expand the chain
            if (keys.Count == 1)
            {
                return parent.GetOrCreateKey(keys[0]);
            }
            
            var currentObj = parent;
            while (keys.Count > 1)
            {
                var key = keys.Pop();
                var currentValue = currentObj.GetOrCreateKey(key);
                if (currentValue.IsObject())
                {
                    currentObj = currentValue.GetObject();
                }
                else
                {
                    currentObj = new HoconObject();
                    currentValue.AppendValue(currentObj);
                }
            }

            return currentObj.GetOrCreateKey(keys[0]);
        }
        
        private static IHoconElement ToHoconElement(this IConfigurationSection config, bool normalizeKeys)
        {
            if (config.IsArray())
            {
                var array = new HoconArray();
                foreach (var child in config.GetChildren().OrderBy(c => int.Parse(c.Key)))
                {
                    var value = new HoconValue();
                    var element = child.ToHoconElement(normalizeKeys);
                    value.AppendValue(element);
                    array.Add(value);
                }
                return array;
            }
            
            if (config.IsObject())
            {
                var rootObject = new HoconObject();
                foreach (var child in config.GetChildren())
                {
                    var value = child.ExpandKey(rootObject, normalizeKeys);
                    value.AppendValue(child.ToHoconElement(normalizeKeys));
                }
                return rootObject;
            }
            
            // Need to back-convert "True" and "False" to "on" and "off"
            return new HoconLiteral
            {
                Value = config.Value switch
                {
                    "True" => "on",
                    "False" => "off",
                    _ => config.Value
                }
            };
        }

        private static string Pop(this IList<string> list)
        {
            var first = list.First();
            list.RemoveAt(0);
            return first;
        }

        private static bool IsObject(this IConfigurationSection config)
            => config.GetChildren().Any() && config.Value == null;

        private static bool IsArray(this IConfiguration config)
        {
            var children = config.GetChildren().ToArray();
            return children.Length > 0 && children.All(c => int.TryParse(c.Key, out _));
        }
    }
}