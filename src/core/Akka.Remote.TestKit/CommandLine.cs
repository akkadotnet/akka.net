//-----------------------------------------------------------------------
// <copyright file="CommandLine.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Specialized;

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
        private readonly static Lazy<StringDictionary> Values = new Lazy<StringDictionary>(() =>
        {
            var dictionary = new StringDictionary();
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (!arg.StartsWith("-D")) continue;
                var tokens = arg.Substring(2).Split('=');
                dictionary.Add(tokens[0], tokens[1]);
            }
            return dictionary;
        });

        public static string GetProperty(string key)
        {
            return Values.Value[key];
        }

        public static int GetInt32(string key)
        {
            return Convert.ToInt32(GetProperty(key));
        }
    }
}

