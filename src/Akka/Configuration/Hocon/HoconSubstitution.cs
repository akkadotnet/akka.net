using System.Collections.Generic;

namespace Akka.Configuration.Hocon
{
    public class HoconSubstitution : IHoconElement, IMightBeAHoconObject
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

        public bool IsObject()
        {
            return ResolvedValue != null && ResolvedValue.IsObject();
        }

        public HoconObject GetObject()
        {
            return ResolvedValue.GetObject();
        }

        #region Implicit operators

        public static implicit operator HoconObject(HoconSubstitution substitution)
        {
            return substitution.GetObject();
        }

        #endregion
    }
}