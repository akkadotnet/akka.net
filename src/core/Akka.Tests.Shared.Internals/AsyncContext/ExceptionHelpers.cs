//-----------------------------------------------------------------------
// <copyright file="ExceptionHelpers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.ExceptionServices;

internal static class ExceptionHelpers
{
    /// <summary>
    /// Attempts to prepare the exception for re-throwing by preserving the stack trace. The returned exception should be immediately thrown.
    /// </summary>
    /// <param name="exception">The exception. May not be <c>null</c>.</param>
    /// <returns>The <see cref="Exception"/> that was passed into this method.</returns>
    public static Exception PrepareForRethrow(Exception exception)
    {
        ExceptionDispatchInfo.Capture(exception).Throw();

        // The code cannot ever get here. We just return a value to work around a badly-designed API (ExceptionDispatchInfo.Throw):
        //  https://connect.microsoft.com/VisualStudio/feedback/details/689516/exceptiondispatchinfo-api-modifications (http://www.webcitation.org/6XQ7RoJmO)
        return exception;
    }
}
