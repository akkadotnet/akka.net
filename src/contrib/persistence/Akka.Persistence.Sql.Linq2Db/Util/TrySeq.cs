using System;
using System.Collections.Generic;
using System.Linq;
using LanguageExt;

namespace Akka.Persistence.Sql.Linq2Db.Utility
{
    public static class TrySeq
    {
        public static Akka.Util.Try<IEnumerable<T>> Sequence<T>(IEnumerable<Akka.Util.Try<T>> seq) 
        {
            return Akka.Util.Try<IEnumerable<T>>.From(()=>seq.Select(r => r.Get()));
        }
        public static Akka.Util.Try<List<T>> SequenceList<T>(IEnumerable<Akka.Util.Try<T>> seq) 
        {
            try
            {
                return new Util.Try<List<T>>(seq.Select(r => r.Get()).ToList());
            }
            catch (Exception e)
            {
                return new Util.Try<List<T>>(e);
            }
        }
        public static Akka.Util.Try<Seq<T>> SequenceSeq<T>(IEnumerable<Akka.Util.Try<T>> seq) 
        {
            return Akka.Util.Try<Seq<T>>.From(()=>seq.Select(r => r.Get()).ToList().ToSeq());
        }
    }
}