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