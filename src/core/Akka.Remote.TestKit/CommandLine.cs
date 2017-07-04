//-----------------------------------------------------------------------
// <copyright file="CommandLine.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Akka.Remote.TestKit
{   
    //TODO: Needs some work
    /// <summary>
    /// Command line argument parser for individual node tests during a <see cref="MultiNodeSpec"/>.
    /// 
    /// Parses arguments from <see cref="Environment.GetCommandLineArgs"/> using the same conventions as canonical Akka.
    /// 
    /// For example (from the Akka.NodeTestRunner source):
    /// <code>
    ///     var nodeIndex = CommandLine.GetInt32("multinode.index");
    ///     var assemblyName = CommandLine.GetProperty("multinode.test-assembly");
    ///     var typeName = CommandLine.GetProperty("multinode.test-class");
    ///     var testName = CommandLine.GetProperty("multinode.test-method");
    /// </code>
    /// </summary>
    public class CommandLine
    {
        private static readonly Lazy<Dictionary<string, string>> Values = new Lazy<Dictionary<string, string>>(() =>
        {
            var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (!arg.StartsWith("-D")) continue;
                var tokens = arg.Substring(2).Split('=');
                dictionary[tokens[0]] = tokens[1];
            }
            return dictionary;
        });

        public static string GetProperty(string key)
        {
            return Values.Value.TryGetValue(key, out var res) ? res : null;
        }

        public static string GetPropertyOrDefault(string key, string defaultStr)
        {
            return Values.Value.TryGetValue(key, out var res) ? res : defaultStr;
        }

        public static int GetInt32(string key)
        {
            return Convert.ToInt32(GetProperty(key));
        }

        public static int GetInt32OrDefault(string key, int defaultInt)
        {
            return Values.Value.TryGetValue(key, out var res) ? GetInt32(res) : defaultInt;
        }
    }
}

