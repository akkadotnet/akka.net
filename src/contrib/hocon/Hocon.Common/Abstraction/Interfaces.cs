// //-----------------------------------------------------------------------
// // <copyright file="IHocon.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

/*
namespace Akka.Configuration.Hocon
{
    public class AkkaConfigurationSection : System.Configuration.ConfigurationSection
    {
        public AkkaConfigurationSection() { }
        public Akka.Configuration.Config AkkaConfig { get; }
        [System.Configuration.ConfigurationPropertyAttribute("hocon", IsRequired=true)]
        public Akka.Configuration.Hocon.HoconConfigurationElement Hocon { get; set; }
    }
    public abstract class CDataConfigurationElement : System.Configuration.ConfigurationElement
    {
        protected const string ContentPropertyName = "content";
        protected CDataConfigurationElement() { }
        protected override void DeserializeElement(System.Xml.XmlReader reader, bool serializeCollectionKey) { }
    }
    public class HoconConfigurationElement : Akka.Configuration.Hocon.CDataConfigurationElement
    {
        public HoconConfigurationElement() { }
        [System.Configuration.ConfigurationPropertyAttribute("content", IsKey=true, IsRequired=true)]
        public string Content { get; set; }
    }
    public class HoconTokenizer : Akka.Configuration.Hocon.Tokenizer
    {
        public HoconTokenizer(string text) { }
        public bool IsArrayEnd() { }
        public bool IsArrayStart() { }
        public bool IsAssignment() { }
        public bool IsComma() { }
        public bool IsDot() { }
        public bool IsEndOfObject() { }
        public bool IsInclude() { }
        public bool IsObjectStart() { }
        public bool IsSpaceOrTab() { }
        public bool IsStartOfComment() { }
        public bool IsStartOfQuotedText() { }
        public bool IsStartOfTripleQuotedText() { }
        public bool IsStartSimpleValue() { }
        public bool IsSubstitutionStart() { }
        public bool IsUnquotedKey() { }
        public bool IsUnquotedKeyStart() { }
        public bool IsWhitespace() { }
        public bool IsWhitespaceOrComment() { }
        public Akka.Configuration.Hocon.Token PullArrayEnd() { }
        public Akka.Configuration.Hocon.Token PullArrayStart() { }
        public Akka.Configuration.Hocon.Token PullAssignment() { }
        public Akka.Configuration.Hocon.Token PullComma() { }
        public Akka.Configuration.Hocon.Token PullComment() { }
        public Akka.Configuration.Hocon.Token PullDot() { }
        public Akka.Configuration.Hocon.Token PullEndOfObject() { }
        public Akka.Configuration.Hocon.Token PullInclude() { }
        public Akka.Configuration.Hocon.Token PullNext() { }
        public Akka.Configuration.Hocon.Token PullQuotedKey() { }
        public Akka.Configuration.Hocon.Token PullQuotedText() { }
        public string PullRestOfLine() { }
        public Akka.Configuration.Hocon.Token PullSimpleValue() { }
        public Akka.Configuration.Hocon.Token PullSpaceOrTab() { }
        public Akka.Configuration.Hocon.Token PullStartOfObject() { }
        public Akka.Configuration.Hocon.Token PullSubstitution() { }
        public Akka.Configuration.Hocon.Token PullTripleQuotedText() { }
        public Akka.Configuration.Hocon.Token PullUnquotedKey() { }
        public Akka.Configuration.Hocon.Token PullValue() { }
        public void PullWhitespaceAndComments() { }
    }
    public class Parser
    {
        public Parser() { }
        public static Akka.Configuration.Hocon.HoconRoot Parse(string text, System.Func<string, Akka.Configuration.Hocon.HoconRoot> includeCallback) { }
        public Akka.Configuration.Hocon.HoconArray ParseArray(string currentPath) { }
        public void ParseValue(Akka.Configuration.Hocon.HoconValue owner, string currentPath) { }
    }
    public class Token
    {
        protected Token() { }
        public Token(Akka.Configuration.Hocon.TokenType type) { }
        public Token(string value) { }
        public Akka.Configuration.Hocon.TokenType Type { get; set; }
        public string Value { get; set; }
        public static Akka.Configuration.Hocon.Token Key(string key) { }
        public static Akka.Configuration.Hocon.Token LiteralValue(string value) { }
        public static Akka.Configuration.Hocon.Token Substitution(string path) { }
    }
    public enum TokenType
    {
        Comment = 0,
        Key = 1,
        LiteralValue = 2,
        Assign = 3,
        ObjectStart = 4,
        ObjectEnd = 5,
        Dot = 6,
        EoF = 7,
        ArrayStart = 8,
        ArrayEnd = 9,
        Comma = 10,
        Substitute = 11,
        Include = 12,
    }
    public class Tokenizer
    {
        public Tokenizer(string text) { }
        public bool EoF { get; }
        public bool Matches(string pattern) { }
        public bool Matches(params string[] patterns) { }
        public char Peek() { }
        protected string PickErrorLine(out int index) { }
        public void Pop() { }
        public void PullWhitespace() { }
        public void Push() { }
        public string Take(int length) { }
        public char Take() { }
    }
}  
 */

using System;
using System.Collections.Generic;

namespace Hocon.Abstraction
{
    public interface IHoconElement
    {
        IEnumerable<IHoconValue> GetArray();
        string? GetString();
        bool IsArray();
        bool IsString();
    }
    
    public interface IMightBeAHoconObject
    {
        IHoconObject? GetObject();
        bool IsObject();
    }
    
    public interface IHoconValue : IMightBeAHoconObject
    {
        bool IsEmpty { get; }
        IList<IHoconElement> Values { get; }
        void AppendValue(IHoconElement value);
        void Clear();
        IList<IHoconValue> GetArray();
        bool GetBoolean();
        IList<bool> GetBooleanList();
        byte GetByte();
        IList<byte> GetByteList();
        long? GetByteSize();
        IHoconValue? GetChildObject(string key);
        decimal GetDecimal();
        IList<decimal> GetDecimalList();
        double GetDouble();
        IList<double> GetDoubleList();
        float GetFloat();
        IList<float> GetFloatList();
        int GetInt();
        IList<int> GetIntList();
        long GetLong();
        IList<long> GetLongList();
        string? GetString();
        IList<string> GetStringList();
        TimeSpan GetTimeSpan(bool allowInfinite = true);
        bool IsArray();
        bool IsString();
        void NewValue(IHoconElement value);
        string ToString(int indent);
    }    
    
    public interface IHoconArray : IHoconElement
    {
    }
    
    public interface IHoconObject : IHoconElement
    {
        IDictionary<string, IHoconValue> Items { get; }
        IDictionary<string, object> Unwrapped { get; }
        IHoconValue? GetKey(string key);
        IHoconValue GetOrCreateKey(string key);
        void Merge(IHoconObject other);
        string ToString(int indent);
    }
    
    public interface IHoconLiteral : IHoconElement
    {
        string Value { get; }
    }
    
    public interface IHoconSubstitution : IHoconElement, IMightBeAHoconObject
    {
        string Path { get; }
        IHoconValue? ResolvedValue { get; }
    }
    
    public interface IHoconRoot
    {
        IEnumerable<IHoconSubstitution> Substitutions { get; }
        IHoconValue Value { get; }
        IHoconValue? GetNode(string path);
    }
}