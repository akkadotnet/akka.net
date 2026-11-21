//-----------------------------------------------------------------------
// <copyright file="RemoteHostOutputSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Akka.MultiNode.TestAdapter.Tests.Internal
{
    /// <summary>
    /// A node's last lines of output, often its failure record, must reach the runner before the
    /// runner hears that the node exited.
    /// </summary>
    public class RemoteHostOutputSpec
    {
        // Few enough lines to fit in the pipe buffer, so the child never blocks and exits
        // while most of them are still unread.
        private const int LineCount = 200;
        private const string LastLine = "LAST-LINE";

        // Runs in the child process. Writes its output and exits at once.
        public static Task<int> WriteLinesThenExit(string[] args)
        {
            for (var i = 0; i < LineCount; i++)
                Console.Out.WriteLine($"line {i}");
            Console.Error.WriteLine(LastLine);
            Console.Out.WriteLine(LastLine);
            Console.Out.Flush();
            Console.Error.Flush();
            return Task.FromResult(7);
        }

        [Fact(DisplayName = "RemoteHost delivers all stdout and stderr lines before it reports the exit")]
        public async Task Should_deliver_all_output_before_reporting_exit()
        {
            var stdout = new ConcurrentQueue<string>();
            var stderr = new ConcurrentQueue<string>();
            int? exitCode = null;
            var stdoutCountAtExit = -1;
            var stderrCountAtExit = -1;

            var (_, task) = RemoteHost.RemoteHost.RunProcessAsync(WriteLinesThenExit, Array.Empty<string>(), opt =>
            {
                // A slow handler, like the runner's, which parses and forwards every line. It keeps
                // lines waiting in the pipe after the child has exited.
                opt.OutputDataReceived = (_, e) =>
                {
                    if (e.Data == null) return;
                    Thread.Sleep(1);
                    stdout.Enqueue(e.Data);
                };
                opt.ErrorDataReceived = (_, e) => { if (e.Data != null) stderr.Enqueue(e.Data); };
                opt.OnExit = p =>
                {
                    exitCode = p.ExitCode;
                    stdoutCountAtExit = stdout.Count;
                    stderrCountAtExit = stderr.Count;
                };
            });

            (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)))).Should().BeSameAs(task, "the child process should exit");

            exitCode.Should().Be(7);
            stdoutCountAtExit.Should().Be(LineCount + 1, "OnExit must run after every stdout line was delivered");
            stderrCountAtExit.Should().Be(1, "OnExit must run after every stderr line was delivered");
            stdout.Last().Should().Be(LastLine);
            stderr.Single().Should().Be(LastLine);
        }
    }
}
