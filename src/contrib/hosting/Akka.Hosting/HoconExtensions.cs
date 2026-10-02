// -----------------------------------------------------------------------
//  <copyright file="StringExtensions.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Akka.Configuration;

namespace Akka.Hosting
{
    public static class HoconExtensions
    {
        private static readonly Regex EscapeRegex = new ("[][$\"\\\\{}:=,#`^?!@*&]{1}", RegexOptions.Compiled);
        
        public static string ToHocon(this string? text)
        {
            // nullable literal value support
            if (text is null)
                return "null";

            text = text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("/", "\\/")
                .Replace("\b", "\\b")
                .Replace("\f", "\\f")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");

            var needReplacement = new HashSet<char>(text.Where(c => c > 255));
            foreach (var c in needReplacement)
            {
                text = text.Replace($"{c}", $"\\u{(int)c:x4}");
            }
            
            // double quote support
            if (EscapeRegex.IsMatch(text) && !text.IsQuoted())
                text = $"\"{text}\"";

            if (text == string.Empty)
                text = "\"\"";
            
            return text;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsQuoted(this string str)
        {
            if (str.Length < 2)
                return false;
            if (str.Length == 2 && str == "\"\"")
                return true;

            return str[0] == '"' && str[1] != '"' && str[str.Length - 1] == '"' && str[str.Length - 2] != '"';
        }

        public static string ToHocon(this bool? value)
        {
            if (value is null)
                throw new ConfigurationException("Value can not be null");
            
            return value.Value ? "on" : "off";
        }

        public static string ToHocon(this bool value)
            => value ? "on" : "off";

        public static string ToHocon(this TimeSpan? value, bool allowInfinite = false, bool zeroIsInfinite = false)
        {
            if (value is null)
                throw new ConfigurationException("Value can not be null");
            return value.Value.ToHocon(allowInfinite, zeroIsInfinite);
        }

        public static string ToHocon(this TimeSpan value, bool allowInfinite = false, bool zeroIsInfinite = false)
        {
            if (!allowInfinite)
            {
                if ((zeroIsInfinite && value <= TimeSpan.Zero) || (!zeroIsInfinite && value < TimeSpan.Zero))
                    throw new ConfigurationException("Infinite value is not allowed");
            }

            if ((zeroIsInfinite && value <= TimeSpan.Zero) || (!zeroIsInfinite && value < TimeSpan.Zero))
                return "infinite";
            
            return value.TotalMilliseconds.ToString(CultureInfo.InvariantCulture);
        }

        public static string ToHocon(this float? value)
        {
            if(value is null)
                throw new ConfigurationException("Value can not be null");
            return ToHocon(value.Value);
        }
        
        public static string ToHocon(this float value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
        
        public static string ToHocon(this double? value)
        {
            if(value is null)
                throw new ConfigurationException("Value can not be null");
            return ToHocon(value.Value);
        }
        
        public static string ToHocon(this double value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
        
        public static string ToHocon(this int? value)
        {
            if(value is null)
                throw new ConfigurationException("Value can not be null");
            return ToHocon(value.Value);
        }
        
        public static string ToHocon(this int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
        
        public static string ToHocon(this long? value)
        {
            if(value is null)
                throw new ConfigurationException("Value can not be null");
            return ToHocon(value.Value);
        }
        
        public static string ToHocon(this long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}