namespace Akka.Tools.MatchHandler
{
    public enum HandlerKind
    {
        /// <summary>The handler is a Action&lt;T&gt;</summary>
        Action,

        /// <summary>The handler is a Action&lt;T&gt; and a Predicate&lt;T&gt; is specified</summary>
        ActionWithPredicate, 

        /// <summary>The handler is a Func&lt;T, bool&gt;</summary>
        Func
    };
}