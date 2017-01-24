﻿//-----------------------------------------------------------------------
// <copyright file="Query.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Persistence.Sql.Common.Queries
{
    /// <summary>
    /// TBD
    /// </summary>
    [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
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
    [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
    public sealed class Query: IEquatable<Query>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly object QueryId;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly ISet<IHint> Hints;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="queryId">TBD</param>
        /// <param name="hints">TBD</param>
        /// <exception cref="ArgumentNullException">TBD</exception>
        public Query(object queryId, ISet<IHint> hints)
        {
            if(hints == null)
                throw new ArgumentNullException("hints", "Query expects set of hints passed not to be null");

            QueryId = queryId;
            Hints = hints;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="queryId">TBD</param>
        /// <param name="hints">TBD</param>
        public Query(object queryId, params IHint[] hints) : this(queryId, new HashSet<IHint>(hints)) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(Query other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(QueryId, other.QueryId) && Hints.SetEquals(other.Hints);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as Query);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((QueryId != null ? QueryId.GetHashCode() : 0) * 397) ^ Hints.GetHashCode();
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
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
    [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
    public sealed class QueryResponse : IQueryReply, IEquatable<QueryResponse>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly object QueryId;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly IPersistentRepresentation Message;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="queryId">TBD</param>
        /// <param name="message">TBD</param>
        public QueryResponse(object queryId, IPersistentRepresentation message)
        {
            QueryId = queryId;
            Message = message;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(QueryResponse other)
        {
            if (Equals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(QueryId, other.QueryId) && Equals(Message, other.Message);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as QueryResponse);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((QueryId != null ? QueryId.GetHashCode() : 0) * 397) ^ (Message != null ? Message.GetHashCode() : 0);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return string.Format("QueryResponse<id: {0}, payload: {1}>", QueryId, Message);
        }
    }

    /// <summary>
    /// Message send back from SQL-based journal, when <see cref="Query"/> has been successfully responded.
    /// </summary>
    [Serializable]
    [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
    public sealed class QuerySuccess : IQueryReply, IEquatable<QuerySuccess>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly object QueryId;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="queryId">TBD</param>
        public QuerySuccess(object queryId)
        {
            QueryId = queryId;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(QuerySuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(QueryId, other.QueryId);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as QuerySuccess);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            return (QueryId != null ? QueryId.GetHashCode() : 0);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return string.Format("QuerySuccess<id: {0}>", QueryId);
        }
    }

    /// <summary>
    /// Message send back from SQL-based journal to <see cref="Query"/> sender, when the query execution has failed.
    /// </summary>
    [Serializable]
    [Obsolete("Existing SQL persistence query will be obsoleted, once Akka.Persistence.Query will came out")]
    public sealed class QueryFailure : IQueryReply, IEquatable<QueryFailure>
    {
        /// <summary>
        /// Identifier of the correlated <see cref="Query"/>.
        /// </summary>
        public readonly object QueryId;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Exception Reason;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="queryId">TBD</param>
        /// <param name="reason">TBD</param>
        public QueryFailure(object queryId, Exception reason)
        {
            QueryId = queryId;
            Reason = reason;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(QueryFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(QueryId, other.QueryId) && Equals(Reason, other.Reason);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as QueryFailure);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((QueryId != null ? QueryId.GetHashCode() : 0) * 397) ^ (Reason != null ? Reason.GetHashCode() : 0);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return string.Format("QueryFailure<id: {0}, cause: {1}>", QueryId, Reason);
        }
    }
}