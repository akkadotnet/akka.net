using System;

namespace Akka.Actor
{
    public static class CancelableExtensions
    {
        /// <summary>
        /// If <paramref name="cancelable"/> is not <c>null</c> it's canceled.
        /// </summary>
        /// <param name="cancelable">The cancelable. Will be canceled if it's not <c>null</c></param>
        public static void CancelIfNotNull(this ICancelable cancelable)
        {
            if(cancelable != null) cancelable.Cancel();
        }

    }
}