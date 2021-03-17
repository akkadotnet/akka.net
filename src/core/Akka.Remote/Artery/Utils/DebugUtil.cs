using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Akka.Remote.Artery.Utils
{
    internal static class DebugUtil
    {
        /// <summary>
        /// Prints a message to standard out if the Compile symbol "COMPRESS_DEBUG" has been set.
        /// If the symbol is not set all invocations to this method will be removed by the compiler.
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="args">TBD</param>
        [Conditional("COMPRESS_DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PrintLn(string message, params object[] args)
        {
            var formattedMessage = args.Length == 0 ? message : string.Format(message, args);
            Console.WriteLine("[Compress][{0}][Thread {1:0000}] {2}", DateTime.Now.ToString("o"), Thread.CurrentThread.ManagedThreadId, formattedMessage);
        }
    }
}
