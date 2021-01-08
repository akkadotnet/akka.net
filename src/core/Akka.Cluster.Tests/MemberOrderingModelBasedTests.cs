//-----------------------------------------------------------------------
// <copyright file="MemberOrderingModelBasedTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#if FSCHECK
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Tests.Shared.Internals.Helpers;
using Akka.Util.Internal;
using FsCheck;
using FsCheck.Experimental;
using FsCheck.Xunit;

namespace Akka.Cluster.Tests
{
    public class MemberOrderingModelBasedTests
    {
        public MemberOrderingModelBasedTests()
        {
            // register the custom generators to make testing easier
            Arb.Register<ClusterGenerators>();
        }

        [Property]
        public Property MemberOrderingMustObeyModel()
        {
            return new MembershipMachine().ToProperty();
        }

        [Property(MaxTest = 1000)]
        public Property DistinctMemberAddressesMustCompareDifferently(Address[] addresses)
        {
            if (addresses.Length == 0)
                return true.Classify(true, "empty set");
            var addressCount = addresses.Length;
            var distinctCount = addresses.Distinct().Count(); // uses `Address.Equals` under the hood
            if (addressCount != distinctCount)
                return true.Classify(false, "all distinct").Classify(false, "empty set");

            // use the sort to help us short-circuit a comparison problem
            var sortedAddresses = addresses.OrderBy(x => x, Member.AddressOrdering).ToList();

            var a1 = sortedAddresses.First();
            foreach (var a2 in sortedAddresses.Skip(1))
            {
                if (Member.AddressOrdering.Compare(a1, a2) == 0)
                    return false.Classify(true, "all distinct").Classify(false, "empty set")
                        .Label($"Expected {a1} and {a2} to compare as different, but did not. Compared as same");
                a1 = a2; // next member
            }

            return true.Classify(true, "all distinct").Classify(false, "empty set"); ;
        }
    }

    public class MembershipMachine : Machine<MembershipState, MembershipModel>
    {
        public MembershipMachine()
        {
            // register the custom generators to make testing easier
            Arb.Register<ClusterGenerators>();
        }

        public override Gen<Operation<MembershipState, MembershipModel>> Next(MembershipModel model)
        {
            if (model.AllMembers.Count == 0)
                return AddNewMember.Generator();
            return Gen.OneOf(ChangeMemberStatus.Generator(model.AllMembers.Keys), AddNewMember.Generator());
        }

        public override Arbitrary<Setup<MembershipState, MembershipModel>> Setup => Arb.From(Arb.Generate<MembershipSetup>()
            .Select(x => (Setup<MembershipState, MembershipModel>)x)); // compiler ceremony :(

        #region Setup

        public class MembershipSetup : Setup<MembershipState, MembershipModel>
        {
            private readonly Member[] _members;

            public MembershipSetup(UniqueAddress[] addresses)
            {
                // filter out any duplicates
                _members =
                    addresses.Distinct()
                        .Select(x => new Member(x, int.MaxValue, MemberStatus.Up, ImmutableHashSet<string>.Empty))
                        .ToArray();
            }

            public override MembershipState Actual()
            {
                return new MembershipState() { Members = ImmutableSortedSet<Member>.Empty.Union(_members) };
            }

            public override MembershipModel Model()
            {
                return new MembershipModel(_members.ToImmutableDictionary(x => x.Address, x => x));
            }

            public override string ToString()
            {
                return $"{GetType()}(Members = [{string.Join(",", _members.Select(x => x.ToString()))}]";
            }
        }

        #endregion

        #region Operations

        public class ChangeMemberStatus : Operation<MembershipState, MembershipModel>
        {
            public static Gen<Operation<MembershipState, MembershipModel>> Generator(IEnumerable<Address> addresses)
            {
                var statusGen = ClusterGenerators.MemberStatusGenerator().Generator;
                Func<Address, MemberStatus, Operation<MembershipState, MembershipModel>> generator =
                    (address, status) => new ChangeMemberStatus(address, status);

                var producer = FsharpDelegateHelper.Create(generator);

                return Gen.Map2(producer, Gen.Elements(addresses), statusGen);
            }

            public readonly Address TargetedMember;
            public readonly MemberStatus NewStatus;

            public ChangeMemberStatus(Address targetedMember, MemberStatus newStatus)
            {
                TargetedMember = targetedMember;
                NewStatus = newStatus;
            }

            public override bool Pre(MembershipModel model)
            {
                var containsMember = model.AllMembers.ContainsKey(TargetedMember);
                if (!containsMember) return false; // short-circuit

                var m = model.AllMembers[TargetedMember];
                return Member.AllowedTransitions[m.Status].Contains(NewStatus);
            }

            public override Property Check(MembershipState actual, MembershipModel model)
            {
                var members = actual.Members;

                var mergedMembers = Member.PickNextTransition(members, model.AllMembers.Values).ToImmutableSortedSet();

                actual.Members = mergedMembers;
                return
                    mergedMembers.SetEquals(model.AllMembers.Values)
                        .Label("Merged members should equal predicted members.")
                        .And(mergedMembers.Single(x => x.Address.Equals(TargetedMember)).Status.Equals(NewStatus))
                        .Label($"Expected Member [{TargetedMember}] to have status [{NewStatus}] but was [{mergedMembers.Single(x => x.Address.Equals(TargetedMember)).Status}]");
            }

            public override MembershipModel Run(MembershipModel model)
            {
                var m = model.AllMembers[TargetedMember];
                return model.UpdateMember(m.Copy(NewStatus));
            }

            public override string ToString()
            {
                return $"{GetType()}(Address = {TargetedMember}, TargetStatus={NewStatus})";
            }
        }

        public class AddNewMember : Operation<MembershipState, MembershipModel>
        {
            public static Gen<Operation<MembershipState, MembershipModel>> Generator()
            {
                return Arb.Generate<AddNewMember>().Select(x => (Operation<MembershipState, MembershipModel>)x);
            }

            private readonly UniqueAddress _address;

            public AddNewMember(UniqueAddress address)
            {
                _address = address;
            }

            public override bool Pre(MembershipModel model)
            {
                var containsMember = model.AllMembers.ContainsKey(_address.Address);
                if (!containsMember) return true; // short-circuit

                // check to see if UniqueAddress already exists (simulates a restart)
                var member = model.AllMembers[_address.Address];
                return !member.UniqueAddress.Equals(_address);
            }

            public override Property Check(MembershipState actual, MembershipModel model)
            {
                var members = actual.Members;
                actual.Members = members.Add(new Member(_address, int.MaxValue, MemberStatus.Up,
                    ImmutableHashSet<string>.Empty));

                var except = actual.Members.SymmetricExcept(model.AllMembers.Values);

                return
                    actual.Members.SetEquals(model.AllMembers.Values)
                        .Label($"Both sets should contain same set of members. Instead found members not included in both sequences: [{string.Join(",", except)}]");
            }

            public override MembershipModel Run(MembershipModel model)
            {
                return
                    model.UpdateMember(new Member(_address, int.MaxValue, MemberStatus.Up,
                        ImmutableHashSet<string>.Empty));
            }

            public override string ToString()
            {
                return $"{GetType()}(Address = {_address})";
            }
        }

        #endregion
    }

    public class MembershipState
    {
        public ImmutableSortedSet<Member> Members { get; set; }
    }

    public class MembershipModel
    {
        public MembershipModel(ImmutableDictionary<Address, Member> allMembers)
        {
            AllMembers = allMembers;
        }

        public ImmutableDictionary<Address, Member> AllMembers { get; }

        public ImmutableSortedSet<Member> MembersByAge
            => ImmutableSortedSet<Member>.Empty.Union(AllMembers.Values).WithComparer(Member.AgeOrdering);

        public MembershipModel UpdateMember(Member m)
        {
            if (AllMembers.ContainsKey(m.Address))
                return new MembershipModel(AllMembers.Remove(m.Address).SetItem(m.Address, m));
            return new MembershipModel(AllMembers.Add(m.Address, m));
        }
    }
}
#endif
