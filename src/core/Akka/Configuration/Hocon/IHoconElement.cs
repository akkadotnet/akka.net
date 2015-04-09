//-----------------------------------------------------------------------
// <copyright file="IHoconElement.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.Configuration.Hocon
{
    /// <summary>
    ///     Marker interface to make it easier to retrieve Hocon objects for substitutions
    /// </summary>
    public interface IMightBeAHoconObject
    {
        bool IsObject();

        HoconObject GetObject();
    }

    public interface IHoconElement
    {
        bool IsString();
        string GetString();

        bool IsArray();

        IList<HoconValue> GetArray();
    }
}

