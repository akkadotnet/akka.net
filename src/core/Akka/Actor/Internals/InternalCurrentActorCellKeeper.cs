//-----------------------------------------------------------------------
// <copyright file="InternalCurrentActorCellKeeper.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Remoting.Messaging;

namespace Akka.Actor.Internal
{
    /// <summary>INTERNAL!
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public static class InternalCurrentActorCellKeeper
    {
        [ThreadStatic]
        private static ActorCell _current;

        [ThreadStatic]
        private static bool _useThreadStatic;

        private readonly static string CallContextKey = "akka.actorcell.current";

        public static bool UseThreadStatic
        {
            get { return _useThreadStatic; }
            set { _useThreadStatic = value; }
        }

        /// <summary>INTERNAL!
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static ActorCell Current
        {
            get
            {
                if (_useThreadStatic)
                    return _current;
                else
                    return CallContext.LogicalGetData(CallContextKey) as ActorCell;
            }
            set
            {
                if (_useThreadStatic)
                    _current = value;
                else
                    CallContext.LogicalSetData(CallContextKey, value);
            }
        }
    }
}

