//-----------------------------------------------------------------------
// <copyright file="CommandLine.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Akka.Configuration;

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
        private static readonly StringDictionary Values;

        static CommandLine()
        {
            Values = new StringDictionary();

            // Detect and fix PowerShell command line input.
            // PowerShell splits command line arguments on '.'
            var args = Environment.GetCommandLineArgs();
            var fixedArgs = new List<string>();
            for (var i = 1; i < args.Length - 1; ++i)
            {
                if (args[i].Equals("-Dmultinode") && args[i + 1].StartsWith("."))
                {
                    fixedArgs.Add(args[i] + args[i+1]);
                    ++i;
                }
            }
            if(fixedArgs.Count == 0)
                fixedArgs.AddRange(args);

            foreach (var arg in fixedArgs)
            {
                if (!arg.StartsWith("-D"))
                {
                    var a = arg.Trim().ToLowerInvariant();
                    if (a.Equals("-h") || a.Equals("--help"))
                    {
                        ShowHelp = true;
                        return;
                    }
                    if (a.Equals("-v") || a.Equals("--version"))
                    {
                        ShowVersion = true;
                        return;
                    }
                    continue;
                }

                var tokens = arg.Substring(2).Split('=');

                if (tokens.Length == 2)
                {
                    Values.Add(tokens[0], tokens[1]);
                }
                else
                {
                    throw new ConfigurationException($"Command line parameter '{arg}' should follow the pattern [-Dmultinode.<key>=<value>].");
                }
            }
        }

        public static bool ShowHelp { get; private set; }
        public static bool ShowVersion { get; private set; }

        public static string GetProperty(string key)
        {
            return Values[key];
        }

        public static string GetPropertyOrDefault(string key, string defaultStr)
        {
            return Values.ContainsKey(key) ? Values[key] : defaultStr;
        }

        public static int GetInt32(string key)
        {
            return Convert.ToInt32(GetProperty(key));
        }

        public static int GetInt32OrDefault(string key, int defaultInt)
        {
            return Values.ContainsKey(key) ? GetInt32(key) : defaultInt;
        }
    }
}

