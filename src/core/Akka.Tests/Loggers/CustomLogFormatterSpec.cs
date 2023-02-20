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
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Loggers
{
    public class CustomLogFormatterSpec : AkkaSpec
    {
        // <CustomLogFormatter>
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
        // </CustomLogFormatter>

        // <CustomLogFormatterConfig>
        public static readonly Config Configuration = "akka.logger-formatter = \"Akka.Tests.Loggers.CustomLogFormatterSpec+CustomLogFormatter, Akka.Tests\"";
        // </CustomLogFormatterConfig>
        
        public CustomLogFormatterSpec(ITestOutputHelper output) : base(Configuration, output)
        {
        }
        
        [Fact]
        public async Task ShouldUseValidCustomLogFormatter()
        {
            // arrange
            var probe = CreateTestProbe();
            Sys.EventStream.Subscribe(probe.Ref, typeof(Error));
            
            // act
            Sys.Log.Error("This is a test {0}", 1); // formatters aren't used when we're logging const strings
            
            // assert
            var msg = await probe.ExpectMsgAsync<Error>();
            msg.Message.Should().BeAssignableTo<LogMessage>();
            msg.ToString().Should().Contain("Custom: This is a test 1");
            
            await EventFilter.Error(contains: "Custom").ExpectOneAsync(() =>
            {
                Sys.Log.Error("This is a test {0}", 1);
                return Task.CompletedTask;
            });
        }
        
        [Fact]
        public async Task ShouldDetectCustomLogFormatterOutputInEventFilter()
        {
            // the EventFilter filters on fully formatted output
            await EventFilter.Error(contains: "Custom").ExpectOneAsync(() =>
            {
                Sys.Log.Error("This is a test {0}", 1);
                return Task.CompletedTask;
            });
        }
    }
}