using System.Collections.Generic;

namespace Akka.Configuration.Hocon
{
    public interface IHoconElement
    {
        bool IsString();
        string GetString();

        bool IsArray();

        IList<HoconValue> GetArray();
    }
}