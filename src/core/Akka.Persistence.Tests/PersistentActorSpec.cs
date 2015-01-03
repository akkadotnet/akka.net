using System;
using System.Collections.Generic;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests
{
    public partial class PersistentActorSpec : PersistenceSpec
    {
        public PersistentActorSpec(string config) : base(config)
        {
        }

        [Fact]
        public void PersistentActor_should_recover_from_persisted_events()
        {
        }

        [Fact]
        public void PersistentActor_should_handle_multiple_emitted_events_in_correct_order_for_single_persist_call()
        {
        }

        [Fact]
        public void PersistentActor_should_handle_multiple_emitted_events_in_correct_order_for_multiple_persist_calls()
        {
        }

        [Fact]
        public void PersistentActor_should_receive_emitted_events_immediately_after_command()
        {
        }

        [Fact]
        public void PersistentActor_should_recover_on_command_failure()
        {
        }

        [Fact]
        public void PersistentActor_should_allow_behavior_changes_in_event_handler_when_handling_the_last_event()
        {
        }

        [Fact]
        public void PersistentActor_should_allow_behavior_changes_in_event_handler_as_first_action()
        {
        }

        [Fact]
        public void PersistentActor_should_allow_behavior_changes_in_event_handler_as_last_action()
        {
        }

        [Fact]
        public void PersistentActor_should_support_snapshotting()
        {
        }

        [Fact]
        public void PersistentActor_should_support_Context_Become_during_recovery()
        {
        }

        [Fact]
        public void PersistentActor_should_be_able_to_reply_within_an_event_handler()
        {
        }

        [Fact]
        public void PersistentActor_should_support_user_stash_operations()
        {
        }

        [Fact]
        public void PersistentActor_should_support_user_stash_operations_with_serveral_stashed_messages()
        {
        }

        [Fact]
        public void PersistentActor_should_support_user_stash_operations_under_failures()
        {
        }

        [Fact]
        public void PersistentActor_should_be_able_to_persist_value_types_as_events()
        {
        }

        [Fact]
        public void PersistentActor_should_be_able_to_opt_out_from_stashing_messages_until_all_events_has_been_processed()
        {
        }

        [Fact]
        public void PersistentActor_should_support_mutli_PersistAsync_calls_for_one_command_and_executed_them_when_possible()
        {
        }

        [Fact]
        public void PersistentActor_should_reply_to_the_original_sender_of_a_command_even_on_PersistAsync()
        {
        }

        [Fact]
        public void PersistentActor_should_support_the_same_event_being_PersistAsynced_mutliple_times()
        {
        }

        [Fact]
        public void PersistentActor_should_support_a_mix_of_persist_calls_and_persist_calls_in_expected_order()
        {
        }

        [Fact]
        public void PersistentActor_should_support_a_mix_of_persist_calls_and_persist_calls()
        {
        }

        [Fact]
        public void PersistentActor_should_correlate_PersistAsync_handlers_after_restart()
        {
        }

        [Fact]
        public void PersistentActor_should_allow_deferring_handlers_in_order_to_provide_ordered_processing_in_respect_to_persist_handlers()
        {
        }

        [Fact]
        public void PersistentActor_should_invoke_deffered_handlers_in_presence_of_mixed_a_long_series_Persist_and_PersistAsync_calls()
        {
        }

        [Fact]
        public void PersistentActor_should_invoke_deffered_handlers_right_away_if_there_are_no_persist_handlers_registered()
        {
        }

        [Fact]
        public void PersistentActor_should_invoke_deffered_handlers_preserving_the_original_sender_reference()
        {
        }

        [Fact]
        public void PersistentActor_should_receiver_RecoveryFinished_if_its_handled_after_all_events_have_been_replayed()
        {
        }

        [Fact]
        public void PersistentActor_should_preserve_order_of_incoming_messages()
        {
        }

        [Fact]
        public void PersistentActor_should_be_used_as_a_stackable_modification()
        {
        }
    }
}