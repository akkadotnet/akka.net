using System.Collections.Generic;

namespace Akka.Configuration.Hocon
{
    public class HoconSubstitution : IHoconElement
    {
        public HoconSubstitution(string path)
        {
            Path = path;
        }

        public string Path { get; private set; }

        public HoconValue ResolvedValue { get; set; }

        public bool IsString()
        {
            return ResolvedValue.IsString();
        }

        public string GetString()
        {
            return ResolvedValue.GetString();
        }

        public bool IsArray()
        {
            return ResolvedValue.IsArray();
        }

        public IList<HoconValue> GetArray()
        {
            return ResolvedValue.GetArray();
        }
    }
}