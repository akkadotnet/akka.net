//-----------------------------------------------------------------------
// <copyright file="HoconObject.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Hocon.Abstraction;
using Newtonsoft.Json;

namespace Hocon
{
    /// <summary>
    /// This class represents an object element in a HOCON (Human-Optimized Config Object Notation)
    /// configuration string.
    /// <code>
    /// akka {  
    ///   actor {
    ///     debug {  
    ///       receive = on 
    ///       autoreceive = on
    ///       lifecycle = on
    ///       event-stream = on
    ///       unhandled = on
    ///     }
    ///   }
    /// }
    /// </code>
    /// </summary>
    public class HoconObject : IHoconObject
    {
        private static readonly Regex EscapeRegex = new Regex("[ \t:]{1}", RegexOptions.Compiled);

        /// <summary>
        /// Initializes a new instance of the <see cref="HoconObject"/> class.
        /// </summary>
        public HoconObject()
        {
            Items = new Dictionary<string, IHoconValue>();
        }

        /// <summary>
        /// Retrieves the underlying map that contains the barebones
        /// object values.
        /// </summary>
        [JsonIgnore]
        public IDictionary<string, object> Unwrapped
        {
            get
            {
                return Items.ToDictionary(k => k.Key, v =>
                {
                    var obj = v.Value.GetObject();
                    if (obj != null)
                        return (object) obj.Unwrapped;
                    return v.Value;
                });
            }
        }

        /// <summary>
        /// Retrieves the underlying map that this element is based on.
        /// </summary>
        public IDictionary<string, IHoconValue> Items { get; private set; }

        /// <summary>
        /// Determines whether this element is a string.
        /// </summary>
        /// <returns><c>false</c></returns>
        public bool IsString()
        {
            return false;
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <returns>N/A</returns>
        /// <exception cref="NotImplementedException">
        /// This exception is thrown automatically since this element is an object and not a string.
        /// </exception>
        public string GetString()
        {
            throw new NotImplementedException("This element is an object and not a string.");
        }

        /// <summary>
        /// Determines whether this element is an array.
        /// </summary>
        /// <returns><c>false</c></returns>
        public bool IsArray()
        {
            return false;
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <returns>N/A</returns>
        /// <exception cref="NotImplementedException">
        /// This exception is thrown automatically since this element is an object and not an array.
        /// </exception>
        public IEnumerable<IHoconValue> GetArray()
        {
            throw new NotImplementedException("This element is an object and not an array.");
        }

        /// <summary>
        /// Retrieves the value associated with the supplied key.
        /// </summary>
        /// <param name="key">The key associated with the value to retrieve.</param>
        /// <returns>
        /// The value associated with the supplied key or null
        /// if they key does not exist.
        /// </returns>
        public IHoconValue? GetKey(string key)
        {
            Items.TryGetValue(key, out var value);
            return value;
        }

        /// <summary>
        /// Retrieves the value associated with the supplied key.
        /// If the supplied key is not found, then one is created
        /// with a blank value.
        /// </summary>
        /// <param name="key">The key associated with the value to retrieve.</param>
        /// <returns>The value associated with the supplied key.</returns>
        public IHoconValue GetOrCreateKey(string key)
        {
            if (Items.TryGetValue(key, out var value))
                return value;
            var child = new HoconValue();
            Items.Add(key, child);
            return child;
        }

        /// <summary>
        /// Returns a HOCON string representation of this element.
        /// </summary>
        /// <returns>A HOCON string representation of this element.</returns>
        public override string ToString()
        {
            return ToString(0);
        }

        /// <summary>
        /// Returns a HOCON string representation of this element.
        /// </summary>
        /// <param name="indent">The number of spaces to indent the string.</param>
        /// <returns>A HOCON string representation of this element.</returns>
        public string ToString(int indent)
        {
            var i = new string(' ', indent*2);
            var sb = new StringBuilder();
            foreach (var kvp in Items)
            {
                var key = QuoteIfNeeded(kvp.Key);
                sb.AppendFormat("{0}{1} : {2}\r\n", i, key, kvp.Value.ToString(indent));
            }
            return sb.ToString();
        }

        private string QuoteIfNeeded(string? text)
        {
            if (text == null) return "";

            if (EscapeRegex.IsMatch(text))
            {
                return "\"" + text + "\"";
            }

            return text;
        }

        /// <summary>
        /// Merges the specified object into this instance.
        /// </summary>
        /// <param name="other">The object to merge into this instance.</param>
        public void Merge(IHoconObject? other)
        {
            if(other == null)
                return;
            
            var thisItems = Items;
            var otherItems = other.Items;
            var modified = new List<KeyValuePair<string, IHoconValue>>();

            foreach (var otherItem in otherItems)
            {
                //if other key was present in this object and if we have a value, 
                //just ignore the other value, unless it is an object
                if (thisItems.TryGetValue(otherItem.Key, out var thisItem))
                {
                    //if both values are objects, merge them
                    if (thisItem.IsObject() && otherItem.Value.IsObject())
                    {
                        var newObject = ((HoconObject?) thisItem.GetObject())!.MergeImmutable(otherItem.Value.GetObject());
                        var value = new HoconValue();
                        value.Values.Add(newObject);
                        modified.Add(new KeyValuePair<string, IHoconValue>(otherItem.Key, value));
                    }
                    else
                        modified.Add(new KeyValuePair<string, IHoconValue>(otherItem.Key, otherItem.Value));
                }
                else
                {
                    //other key was not present in this object, just copy it over
                    modified.Add(new KeyValuePair<string, IHoconValue>(otherItem.Key, otherItem.Value));
                }
            }

            if (modified.Count == 0)
                return;

            foreach(var kvp in modified)
                Items[kvp.Key] = kvp.Value;
        }

        /// <summary>
        /// Merges the specified object with this instance producing new one.
        /// </summary>
        /// <param name="other">The object to merge into this instance.</param>
        public HoconObject MergeImmutable(IHoconObject? other)
        {
            if (other == null)
                return this;
            
            var thisItems = new Dictionary<string, IHoconValue>(Items);
            var otherItems = other.Items;

            foreach (var otherItem in otherItems)
            {
                //if other key was present in this object and if we have a value,
                //just ignore the other value, unless it is an object
                if (thisItems.TryGetValue(otherItem.Key, out var thisItem))
                {
                    //if both values are objects, merge them
                    if (thisItem.IsObject() && otherItem.Value.IsObject())
                    {
                        var mergedObject = ((HoconObject?) thisItem.GetObject())!.MergeImmutable(otherItem.Value.GetObject());
                        var mergedValue = new HoconValue();
                        mergedValue.AppendValue(mergedObject);
                        thisItems[otherItem.Key] = mergedValue;
                    }
                    else
                        thisItems[otherItem.Key] = otherItem.Value;
                }
                else
                {
                    //other key was not present in this object, just copy it over
                    thisItems.Add(otherItem.Key, new HoconValue(otherItem.Value.Values));
                }
            }
            return new HoconObject {Items = thisItems};
        }
    }
}
