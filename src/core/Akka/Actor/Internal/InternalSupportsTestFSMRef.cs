//-----------------------------------------------------------------------
// <copyright file="InternalSupportsTestFSMRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor.Internal
{
    /// <summary>
    /// INTERNAL API. Used for testing.
    /// This is used to let TestFSMRef in TestKit access to internal methods.
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public interface IInternalSupportsTestFSMRef<TState, TData>
    {
        /// <summary>
        /// INTERNAL API. Used for testing.
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        void ApplyState(FSMBase.State<TState, TData> upcomingState);

        /// <summary>
        /// INTERNAL API. Used for testing.
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        bool IsStateTimerActive { get; }
    }

    /// <summary>
    /// INTERNAL API. Used for testing.
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class InternalActivateFsmLogging
    {
        private static readonly InternalActivateFsmLogging _instance = new InternalActivateFsmLogging();

        private InternalActivateFsmLogging(){}
        /// <summary>
        /// INTERNAL API. Used for testing.
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static InternalActivateFsmLogging Instance { get { return _instance; } }
    }
}

