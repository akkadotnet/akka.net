using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
