﻿//-----------------------------------------------------------------------
// <copyright file="IRequiresMessageQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Dispatch
{
    public interface IRequiresMessageQueue<T> where T:ISemantics
    {
    }
}

