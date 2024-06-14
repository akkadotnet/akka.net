// -----------------------------------------------------------------------
//  <copyright file="LogFormatSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Akka.Event;
using FluentAssertions;
using VerifyTests;
using VerifyXunit;
using Xunit;

namespace Akka.API.Tests;

/// <summary>
/// Regression test for https://github.com/akkadotnet/akka.net/issues/7255
///
/// Need to assert that the default log format is still working as expected.
/// </summary>
[UsesVerify]
public sealed class DefaultLogFormatSpec : TestKit.Xunit2.TestKit
{
    public DefaultLogFormatSpec() : base("akka.loglevel = DEBUG")
    {
    }
    
    public class OutputRedirector : IDisposable
    {
        private readonly TextWriter _originalOutput;
        private readonly StreamWriter _writer;

        public OutputRedirector(string filePath)
        {
            _originalOutput = Console.Out;
            _writer = new StreamWriter(filePath)
            {
                AutoFlush = true
            };
            Console.SetOut(_writer);
        }

        public void Dispose()
        {
            Console.SetOut(_originalOutput);
            _writer.Dispose();
        }
    }
    
    [Fact]
    public async Task ShouldUseDefaultLogFormat()
    {
        // arrange
        var filePath = Path.GetTempFileName();
        var probe = CreateTestProbe();
        Sys.EventStream.Subscribe(probe.Ref, typeof(LogEvent));

        // act
        using (new OutputRedirector(filePath))
        {
            Sys.Log.Debug("This is a test {0} {1}", 1, "cheese");
            Sys.Log.Info("This is a test {0}", 1);
            Sys.Log.Warning("This is a test {0}", 1);
            Sys.Log.Error("This is a test {0}", 1);

            try
            {
                throw new Exception("boom!");
            }
            catch (Exception ex)
            {
                Sys.Log.Debug(ex, "This is a test {0} {1}", 1, "cheese");
                Sys.Log.Info(ex, "This is a test {0}", 1);
                Sys.Log.Warning(ex, "This is a test {0}", 1);
                Sys.Log.Error(ex, "This is a test {0}", 1);
            }

            // force all logs to be received
            await probe.ReceiveNAsync(8).ToListAsync();
        }

        // assert
        // var verifySettings = new VerifySettings();
        // verifySettings.UseDirectory("logs");
        // verifySettings.UseFileName("DefaultLogFormatSpec");
        Verifier.VerifyFile(filePath);
    }
}