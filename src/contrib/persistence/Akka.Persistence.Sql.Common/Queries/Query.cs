//-----------------------------------------------------------------------
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
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="hints"/> is undefined.
        /// </exception>
        public Query(object queryId, ISet<IHint> hints)
        {
            if(hints == null)
                throw new ArgumentNullException(nameof(hints), "Query expects set of hints passed not to be null");

            QueryId = queryId;
            Hints = hints;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="queryId">TBD</param>
        /// <param name="hints">TBD</param>
        public Query(object queryId, params IHint[] hints) : this(queryId, new HashSet<IHint>(hints)) { }

        /// <inheritdoc/>
        public bool Equals(Query other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(QueryId, other.QueryId) && Hints.SetEquals(other.Hints);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as Query);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((QueryId != null ? QueryId.GetHashCode() : 0) * 397) ^ Hints.GetHashCode();
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"Query<id: {QueryId}, hints: [{string.Join(",", Hints)}]>";
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

        /// <inheritdoc/>
        public bool Equals(QueryResponse other)
        {
            if (Equals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(QueryId, other.QueryId) && Equals(Message, other.Message);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as QueryResponse);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((QueryId != null ? QueryId.GetHashCode() : 0) * 397) ^ (Message != null ? Message.GetHashCode() : 0);
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"QueryResponse<id: {QueryId}, payload: {Message}>";
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

        /// <inheritdoc/>
        public bool Equals(QuerySuccess other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(QueryId, other.QueryId);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as QuerySuccess);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return (QueryId != null ? QueryId.GetHashCode() : 0);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"QuerySuccess<id: {QueryId}>";
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

        /// <inheritdoc/>
        public bool Equals(QueryFailure other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(QueryId, other.QueryId) && Equals(Reason, other.Reason);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as QueryFailure);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((QueryId != null ? QueryId.GetHashCode() : 0) * 397) ^ (Reason != null ? Reason.GetHashCode() : 0);
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"QueryFailure<id: {QueryId}, cause: {Reason}>";
        }
    }
}