//-----------------------------------------------------------------------
// <copyright file="CommandLine.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;

namespace Akka.Remote.TestKit
{
    /// <summary>
    /// Test argument parser for indivudla node tests during a <see cref="MultiNodeSpec" />.
    /// 
    /// Parses arguments using the same conventions as canonical Akka.
    /// 
    /// Relies on being directly initialized rather than using Environment.GetCommandLineArgs().
    /// This allows us to abstract the runner (AppDomain or Process) from the test.
    /// </summary>
    public static class TestSettings
    {
        private const string CallContextName = "MultiNode settings CallContext";

        public static void Initialize(string[] args)
        {
            var argsDictionary = new Dictionary<string,string>();

            args.Where(arg => arg.StartsWith("-D"))
                .Select(arg => arg.Substring(2).Split('='))
                .Select(tokens => argsDictionary[tokens[0]] = tokens[1])
                .Ignore();

            CallContext.LogicalSetData(CallContextName, argsDictionary);
        }

        public static string GetProperty(string key)
        {
            try
            {
                var args =(Dictionary<string,string>)CallContext.LogicalGetData(CallContextName);
                return args[key];
            }
            catch (KeyNotFoundException)
            {
                return string.Empty;
            }
        }

        public static int GetInt32(string key)
        {
            return Convert.ToInt32(GetProperty(key));
        }
    }

    public static class EnumerableExtensions
    {
        /// <summary>
        /// We just want the side effects.
        /// </summary>
        /// <param name="stream">The source enumerable</param>
        public static void Ignore(this IEnumerable<object> stream)
        {
            var enumerator = stream.GetEnumerator();
            while (enumerator.MoveNext())
            {
                //nop
            }
        }
    }
}