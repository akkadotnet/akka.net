﻿//-----------------------------------------------------------------------
// <copyright file="HandlerKind.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Tools.MatchHandler
{
    public enum HandlerKind
    {
        /// <summary>The handler is a Action&lt;T&gt;</summary>
        Action,

        /// <summary>The handler is a Action&lt;T&gt; and a Predicate&lt;T&gt; is specified</summary>
        ActionWithPredicate, 

        /// <summary>The handler is a Func&lt;T, bool&gt;</summary>
        Func
    };
}

