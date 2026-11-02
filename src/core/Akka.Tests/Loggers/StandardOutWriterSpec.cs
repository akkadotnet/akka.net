//-----------------------------------------------------------------------
// <copyright file="StandardOutWriterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Threading.Tasks;
using Akka.TestKit;
using Akka.Util;
using Xunit;

namespace Akka.Tests.Loggers
{
    /// <summary>
    /// Tests for StandardOutWriter to ensure it handles IIS/Windows Service environments correctly
    /// where Console.Out and Console.Error may be redirected to StreamWriter.Null
    /// </summary>
    public class StandardOutWriterSpec : AkkaSpec
    {
        public StandardOutWriterSpec(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void StandardOutWriter_should_handle_concurrent_writes_without_race_conditions()
        {
            // This test simulates the concurrent access pattern that causes issues in IIS
            // In normal test environments this won't reproduce the issue, but it ensures
            // our fix doesn't break normal console operation
            
            var tasks = new Task[100];
            
            for (int i = 0; i < tasks.Length; i++)
            {
                var taskId = i;
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < 10; j++)
                    {
                        // These calls should not throw even under concurrent access
                        StandardOutWriter.WriteLine($"Task {taskId} - Line {j}");
                        StandardOutWriter.Write($"Task {taskId} - Write {j} ");
                    }
                });
            }

            // Should complete without throwing IndexOutOfRangeException
            Assert.True(Task.WaitAll(tasks, TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public void StandardOutWriter_should_not_throw_when_console_is_redirected()
        {
            // Save original streams
            var originalOut = Console.Out;
            var originalError = Console.Error;
            
            try
            {
                // Simulate IIS/Windows Service environment by redirecting to null
                Console.SetOut(StreamWriter.Null);
                Console.SetError(StreamWriter.Null);
                
                // These should not throw even when console is redirected to null
                StandardOutWriter.WriteLine("This should not throw");
                StandardOutWriter.Write("Neither should this");
                
                // Test with colors (which would normally fail in IIS)
                StandardOutWriter.WriteLine("Colored output", ConsoleColor.Red);
                StandardOutWriter.Write("Colored write", ConsoleColor.Blue, ConsoleColor.Yellow);
            }
            finally
            {
                // Restore original streams
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }

        [Fact]
        public void StandardOutWriter_should_handle_null_and_empty_messages()
        {
            // Should not throw
            StandardOutWriter.WriteLine(null);
            StandardOutWriter.WriteLine("");
            StandardOutWriter.Write(null);
            StandardOutWriter.Write("");
        }
    }
}