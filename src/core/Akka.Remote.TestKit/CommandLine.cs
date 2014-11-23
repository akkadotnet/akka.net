using System;
using System.Collections.Specialized;

namespace Akka.Remote.TestKit
{
    //TODO: Needs some work
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
