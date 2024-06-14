// -----------------------------------------------------------------------
//  <copyright file="LogFormatSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using Akka.Event;
using FluentAssertions;
using VerifyXunit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.API.Tests;

/// <summary>
/// Regression test for https://github.com/akkadotnet/akka.net/issues/7255
///
/// Need to assert that the default log format is still working as expected.
/// </summary>
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
    public void ShouldUseDefaultLogFormat()
    {
        // arrange
        var filePath = Path.GetTempFileName();

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
        }

        // assert
        Verifier.VerifyFile(filePath);
    }
}