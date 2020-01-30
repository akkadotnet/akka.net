using System;
using System.Reflection;

internal static class ExceptionExtensions
{
    /// <summary>
    /// Unwraps an exception to remove any wrappers, like <see cref="TargetInvocationException"/>.
    /// </summary>
    /// <param name="ex">The exception to unwrap.</param>
    /// <returns>The unwrapped exception.</returns>
    public static Exception Unwrap(this Exception ex)
    {
        while (true)
        {
            var tiex = ex as TargetInvocationException;
            if (tiex == null)
                return ex;

            ex = tiex.InnerException;
        }
    }
}
