//-----------------------------------------------------------------------
// <copyright file="AppVersion.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Akka.Configuration;
using Newtonsoft.Json;

namespace Akka.Util
{
    /// <summary>
    ///  Comparable version information.
    ///
    ///  The typical convention is to use 3 digit version numbers `major.minor.patch`,
    ///  but 1 or two digits are also supported.
    ///
    ///  If no `.` is used it is interpreted as a single digit version number or as
    ///  plain alphanumeric if it couldn't be parsed as a number.
    ///
    ///  It may also have a qualifier at the end for 2 or 3 digit version numbers such as "1.2-RC1".
    ///  For 1 digit with qualifier, 1-RC1, it is interpreted as plain alphanumeric.
    ///
    ///  It has support for https://github.com/dwijnand/sbt-dynver format with `+` or
    ///  `-` separator. The number of commits from the tag is handled as a numeric part.
    ///  For example `1.0.0+3-73475dce26` is less than `1.0.10+10-ed316bd024` (3 &lt; 10).
    /// </summary>
    public class AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
    {
        public static readonly AppVersion Zero = new AppVersion("0.0.0");

        // made internal for testing purposes
        internal const string AssemblyVersionMarker = "assembly-version";
        private const int Undefined = 0;

        private int[] _numbers = Array.Empty<int>();
        private string _rest = "";

        [JsonConstructor]
        internal AppVersion(string version)
        {
            Version = version;
        }

        public static AppVersion Create(string version)
        {
            // check to see if we're going to use the assembly-version
            if (version.Equals(AssemblyVersionMarker, StringComparison.InvariantCultureIgnoreCase))
            {
                return AppVersionFromAssemblyVersion();
            }

            var v2 = new AppVersion(version);
            return v2.Parse();
        }

        /// <summary>
        /// INTERNAL API
        /// </summary>
        /// <remarks>
        /// Internal for testing purposes only.
        /// </remarks>
        internal static AppVersion AppVersionFromAssemblyVersion()
        {
            // user hasn't specified AppVersion in HOCON
            // try looking it up via assembly
            var entryAssembly = Assembly.GetEntryAssembly();

            // if the entryAssembly is null (which can happen when we're called from unmanaged code)
            // then fall back to the executing assembly for the version number.
            var targetAssembly = entryAssembly ?? Assembly.GetExecutingAssembly();

            var name = targetAssembly.GetName();
            var v = new AppVersion($"{name.Version.Major}.{name.Version.Minor}.{name.Version.Build}");
            return v.Parse();
        }

        public string Version { get; }

        private AppVersion Parse()
        {
            (int, string) ParseLastPart(string s)
            {
                // for example 2, 2-SNAPSHOT or dynver 2+10-1234abcd
                if (s.Length == 0)
                {
                    return (Undefined, s);
                }
                else
                {
                    var i = s.IndexOf('-');
                    var j = s.IndexOf('+'); // for dynver
                    var k = i == -1 ? j : (j == -1 ? i : Math.Min(i, j));

                    if (k == -1)
                        return (int.Parse(s), "");
                    else
                        return (int.Parse(s.Substring(0, k)), s.Substring(k + 1));
                }
            }

            (int, string) ParseDynverPart(string s)
            {
                // for example SNAPSHOT or dynver 10-1234abcd
                if (string.IsNullOrEmpty(s) || !char.IsDigit(s[0]))
                {
                    return (Undefined, s);
                }
                else
                {
                    var i = s.IndexOf('-');
                    if (i == -1)
                        return (Undefined, s);

                    try
                    {
                        return (int.Parse(s.Substring(0, i)), s.Substring(i + 1));
                    }
                    catch (FormatException)
                    {
                        return (Undefined, s);
                    }
                }
            }

            (int, int, string) ParseLastParts(string s)
            {
                // for example 2, 2-SNAPSHOT or dynver 2+10-1234abcd
                var (lastNumber, rest) = ParseLastPart(s);
                if (rest == "")
                    return (lastNumber, Undefined, rest);
                else
                {
                    var (dynverNumber, rest2) = ParseDynverPart(rest);
                    return (lastNumber, dynverNumber, rest2);
                }
            }

            if (_numbers.Length == 0)
            {
                var nbrs = new int[4];
                var segments = Version.Split('.');

                string rst;

                if (segments.Length == 1)
                {
                    // single digit or alphanumeric
                    var s = segments[0];
                    if (string.IsNullOrEmpty(s))
                        throw new ArgumentOutOfRangeException("Empty version not supported.");
                    nbrs[1] = Undefined;
                    nbrs[2] = Undefined;
                    nbrs[3] = Undefined;
                    if (char.IsDigit(s[0]))
                    {
                        try
                        {
                            nbrs[0] = int.Parse(s);
                            rst = "";
                        }
                        catch (FormatException)
                        {
                            rst = s;
                        }
                    }
                    else
                    {
                        rst = s;
                    }
                }
                else if (segments.Length == 2)
                {
                    // for example 1.2, 1.2-SNAPSHOT or dynver 1.2+10-1234abcd
                    var (n1, n2, rest) = ParseLastParts(segments[1]);
                    nbrs[0] = int.Parse(segments[0]);
                    nbrs[1] = n1;
                    nbrs[2] = n2;
                    nbrs[3] = Undefined;
                    rst = rest;
                }
                else if (segments.Length == 3)
                {
                    // for example 1.2.3, 1.2.3-SNAPSHOT or dynver 1.2.3+10-1234abcd
                    var (n1, n2, rest) = ParseLastParts(segments[2]);
                    nbrs[0] = int.Parse(segments[0]);
                    nbrs[1] = int.Parse(segments[1]);
                    nbrs[2] = n1;
                    nbrs[3] = n2;
                    rst = rest;
                }
                else
                {
                    throw new ArgumentOutOfRangeException($"Only 3 digits separated with '.' are supported. [{Version}]");
                }

                this._rest = rst;
                this._numbers = nbrs;
            }
            return this;
        }

        public int CompareTo(AppVersion other)
        {
            if (Version == other.Version) // String equals without requiring parse
                return 0;
            else
            {
                Parse();
                other.Parse();
                var diff = 0;
                diff = _numbers[0] - other._numbers[0];
                if (diff == 0)
                {
                    diff = _numbers[1] - other._numbers[1];
                    if (diff == 0)
                    {
                        diff = _numbers[2] - other._numbers[2];
                        if (diff == 0)
                        {
                            diff = _numbers[3] - other._numbers[3];
                            if (diff == 0)
                            {
                                if (_rest == "" && other._rest != "")
                                    diff = 1;
                                if (other._rest == "" && _rest != "")
                                    diff = -1;
                                else
                                    diff = _rest.CompareTo(other._rest);
                            }
                        }
                    }
                }
                return diff;
            }
        }

        public bool Equals(AppVersion other)
        {
            return other != null && Version == other.Version;
        }

        public override bool Equals(object obj)
        {
            return base.Equals(obj as AppVersion);
        }

        public static bool operator ==(AppVersion first, AppVersion second)
        {
            if (object.ReferenceEquals(first, null))
                return object.ReferenceEquals(second, null);
            return first.Equals(second);
        }

        public static bool operator !=(AppVersion first, AppVersion second)
        {
            return !(first == second);
        }

        public override int GetHashCode()
        {
            Parse();
            var hashCode = 13;
            hashCode = (hashCode * 397) ^ _numbers[0];
            hashCode = (hashCode * 397) ^ _numbers[1];
            hashCode = (hashCode * 397) ^ _numbers[2];
            hashCode = (hashCode * 397) ^ _numbers[3];
            hashCode = (hashCode * 397) ^ _rest.GetHashCode();
            return hashCode;
        }

        public override string ToString()
        {
            return Version;
        }
    }
}
