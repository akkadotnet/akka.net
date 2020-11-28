using System.Threading.Tasks;
using Akka.Streams.Dsl;
using LanguageExt;

namespace Akka.Persistence.Sql.Linq2Db.Streams
{
    public static class ExtSeq
    {
        public static Sink<TIn, Task<Seq<TIn>>> Seq<TIn>() => Sink.FromGraph(new ExtSeqStage<TIn>());
    }
}