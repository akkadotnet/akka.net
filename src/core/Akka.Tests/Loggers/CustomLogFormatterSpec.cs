//-----------------------------------------------------------------------
// <copyright file="CustomLogFormatterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Loggers
{
    public class CustomLogFormatterSpec : AkkaSpec
    {
        private class CustomLogFormatter : ILogMessageFormatter
        {
            public string Format(string format, params object[] args)
            {
                return string.Format("Custom: " + format, args);
            }

            public string Format(string format, IEnumerable<object> args)
            {
                return string.Format("Custom: " + format, args.ToArray());
            }
        }

        public static readonly Config Configuration = "akka.logger-formatter = \"Akka.Tests.Loggers.CustomLogFormatterSpec+CustomLogFormatter, Akka.Tests\"";
        
        public CustomLogFormatterSpec(ITestOutputHelper output) : base(Configuration, output)
        {
        }
        
        [Fact]
        public async Task ShouldUseValidCustomLogFormatter()
        {
            await EventFilter.Error(contains: "Custom").ExpectOneAsync(() =>
            {
                Sys.Log.Error("This is a test");
                return Task.CompletedTask;
            });
        }
    }
}