//-----------------------------------------------------------------------
// <copyright file="ConsoleLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Util;
using Microsoft.Extensions.Logging;

namespace Akka.Remote.TestKit
{
    internal sealed class ConsoleLoggerProvider : ILoggerProvider
    {
        public void Dispose()
        {
            
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new ConsoleLogger(categoryName);
        }
    }

    internal sealed class ConsoleLogger : ILogger
    {
        private readonly string _name;

        public ConsoleLogger(string name)
        {
            _name = name;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return NoDisposable.Instance;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            StandardOutWriter.WriteLine($"[{_name}][{logLevel}][{DateTime.UtcNow}]{formatter(state, exception)}");
        }

        sealed class NoDisposable : IDisposable
        {
            public static readonly NoDisposable Instance = new NoDisposable();

            private NoDisposable() { }

            public void Dispose() { }
        }
    }
}
