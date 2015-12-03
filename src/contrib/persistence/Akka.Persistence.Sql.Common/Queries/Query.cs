//-----------------------------------------------------------------------
// <copyright file="Query.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Persistence.Sql.Common.Queries
{
    public interface IQueryReply { }

    /// <summary>
    /// Message send to particular SQL-based journal <see cref="IActorRef"/>. It may be parametrized 
    /// using set of hints. SQL-based journal will respond with collection of <see cref="QueryResponse"/> 
    /// messages followed by <see cref="QuerySuccess"/> when  request succeed or the 
    /// <see cref="QueryFailure"/> message when request has failed for some reason.
    /// 
    /// Since SQL journals can store events in linearized fashion, they are able to provide deterministic 
    /// set of events not based on any partition key. Therefore query request don't need to contain 
    /// partition id of the persistent actor.
    /// </summary>
    [Serializable]
    public sealed class Query: IEquatable<Query>
    {
        public readonly object QueryId;
        public readonly ISet<IHint> Hints;
        
        public Query(object queryId, ISet<IHint> hints)
        {
            if(hints == null)
                throw new ArgumentNullException("hints", "Query expects set of hints passed not to be null");

            QueryId = queryId;
            Hints = hints;
        }

        public Query(object queryId, params IHint[] hints) : this(queryId, new HashSet<IHint>(hints)) { }

        public bool Equals(Query other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(QueryId, other.QueryId) && Hints.SetEquals(other.Hints);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Query);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((QueryId != null ? QueryId.GetHashCode() : 0) * 397) ^ Hints.GetHashCode();
            }
        }

        public override string ToString()
        {
            return string.Format("Query<id: {0}, hints: [{1}]>", QueryId, string.Join(",", Hints));
        }
    }

    /// <summary>
    /// Message send back from SQL-based journal to <see cref="Query"/> sender, 
    /// when the query execution has been completed and result is returned.
    /// </summary>
    [Serializable]
    public sealed class QueryResponse : IQueryReply, IEquatable<QueryResponse>
    {
        public readonly object QueryId;
        public readonly IPersistentRepresentation Message;

        public QueryResponse(object queryId, IPersistentRepresentation message)
        {
            QueryId = queryId;
            Message = message;
        }

        public bool Equals(QueryResponse other)
        {
            if (Equals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(QueryId, other.QueryId) && Equals(Message, other.Message);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as QueryResponse);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((QueryId != null ? QueryId.GetHashCode() : 0) * 397) ^ (Message != null ? Message.GetHashCode() : 0);
            }
        }

        public override string ToString()
        {
            return string.Format("QueryResponse<id: {0}, payload: {1}>", QueryId, Message);
        }
    }

    /// <summary>
    /// Message send back from SQL-based journal, when <see cref="Query"/> has been successfully responded.
    /// </summary>
    [Serializable]
    public sealed class QuerySuccess : IQueryReply, IEquatable<QuerySuccess>
    {
        public readonly object QueryId;

        public QuerySuccess(object queryId)
        {
            QueryId = queryId;
        }

        public bool Equals(QuerySuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(QueryId, other.QueryId);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as QuerySuccess);
        }

        public override int GetHashCode()
        {
            return (QueryId != null ? QueryId.GetHashCode() : 0);
        }

        public override string ToString()
        {
            return string.Format("QuerySuccess<id: {0}>", QueryId);
        }
    }

    /// <summary>
    /// Message send back from SQL-based journal to <see cref="Query"/> sender, when the query execution has failed.
    /// </summary>
    [Serializable]
    public sealed class QueryFailure : IQueryReply, IEquatable<QueryFailure>
    {
        /// <summary>
        /// Identifier of the correlated <see cref="Query"/>.
        /// </summary>
        public readonly object QueryId;
        public readonly Exception Reason;

        public QueryFailure(object queryId, Exception reason)
        {
            QueryId = queryId;
            Reason = reason;
        }

        public bool Equals(QueryFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(QueryId, other.QueryId) && Equals(Reason, other.Reason);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as QueryFailure);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((QueryId != null ? QueryId.GetHashCode() : 0) * 397) ^ (Reason != null ? Reason.GetHashCode() : 0);
            }
        }

        public override string ToString()
        {
            return string.Format("QueryFailure<id: {0}, cause: {1}>", QueryId, Reason);
        }
    }
}