using System;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class DeciderBuilderSpec
    {
        [Fact]
        public void DeciderBuilder_should_support_Directive_and_func_in_collection_initializer()
        {
            var decider = new DeciderBuilder()
            {
                {typeof(ExceptionA), e=> e is ExceptionASub ? Directive.Restart : Directive.Escalate},
                {typeof(ExceptionASubSub), Directive.Resume},

            }.Build();
            decider(new ExceptionA()).ShouldBe(Directive.Escalate);
            decider(new ExceptionASub()).ShouldBe(Directive.Restart);
            decider(new ExceptionASubSub()).ShouldBe(Directive.Resume);
        }

        [Fact]
        public void DeciderBuilder_should_pick_the_most_specific_type_first()
        {
            var decider = new DeciderBuilder()
            {
                {typeof(ExceptionA), Directive.Escalate},
                {typeof(ExceptionASubSub), Directive.Resume},
                {typeof(ExceptionASub), Directive.Restart},
                {typeof(Exception), Directive.Stop}

            }.Build();
            decider(new ExceptionA()).ShouldBe(Directive.Escalate);
            decider(new ExceptionASub()).ShouldBe(Directive.Restart);
            decider(new ExceptionASubSub()).ShouldBe(Directive.Resume);
            decider(new Exception()).ShouldBe(Directive.Stop);
        }

        [Fact]
        public void DeciderBuilder_should_pick_more_general_types()
        {
            var decider = new DeciderBuilder()
            {
                {typeof(ExceptionASub), Directive.Restart},
                {typeof(Exception), Directive.Stop},
            }.Build();

            decider(new ExceptionA()).ShouldBe(Directive.Stop);           //Use Exception
            decider(new ExceptionASub()).ShouldBe(Directive.Restart);     //Use ExceptionASub
            decider(new ExceptionASubSub()).ShouldBe(Directive.Restart);  //Use ExceptionASub
            decider(new Exception()).ShouldBe(Directive.Stop);            //Use Exception
        }

        [Fact]
        public void DeciderBuilder_should_fallback_to_defaultValue_when_no_mapping_match()
        {
            var decider = new DeciderBuilder(fallback:Directive.Escalate)
            {
                {typeof(ExceptionA), Directive.Restart},
            }.Build();

            decider(new ExceptionA()).ShouldBe(Directive.Restart);        //Use ExceptionA
            decider(new ExceptionASub()).ShouldBe(Directive.Restart);     //Use ExceptionA
            decider(new ExceptionASubSub()).ShouldBe(Directive.Restart);  //Use ExceptionA
            decider(new Exception()).ShouldBe(Directive.Escalate);        //Use fallback
            decider(new ExceptionB()).ShouldBe(Directive.Escalate);       //Use fallback
            decider(new ActorInitializationException()).ShouldBe(Directive.Escalate); //Use fallback
        }


        [Fact]
        public void DeciderBuilder_WithFallback_should_call_fallback_when_no_mapping_match()
        {
            var decider = new DeciderBuilder(fallback: e => Directive.Resume)
            {
                {typeof(ExceptionA), Directive.Restart},
            }.Build();

            decider(new ExceptionASub()).ShouldBe(Directive.Restart);               //Use ExceptionA
            decider(new Exception()).ShouldBe(Directive.Resume);                    //Use fallback
            decider(new ExceptionB()).ShouldBe(Directive.Resume);                   //Use fallback
            decider(new ActorInitializationException()).ShouldBe(Directive.Resume); //Use fallback
        }

        [Fact]
        public void DeciderBuilder_WithNoFallback_should_call_DefaultDecider_when_no_mapping_match()
        {
            var decider = new DeciderBuilder
            {
                {typeof(ExceptionA), Directive.Resume},
            }.Build();

            decider(new ExceptionASub()).ShouldBe(Directive.Resume);                 //Use ExceptionA mapping
            decider(new ActorInitializationException()).ShouldBe(Directive.Stop);    //Use DefaultDecider
            decider(new ExceptionB()).ShouldBe(Directive.Restart);                   //Use DefaultDecider
        }

        [Fact]
        public void After_a_Decider_has_been_built_it_cannot_be_modified_by_modifying_the_builder()
        {
            var builder = new DeciderBuilder(Directive.Escalate); //Escalate everything
            var decider = builder.Build();
            
            //Modify the builder
            builder.Add<Exception>(Directive.Stop); //Stop everything

            //Test that we get the value that was there when we built the decider
            decider(new Exception()).ShouldBe(Directive.Escalate);
        }

        [Fact]
        public void A_builder_can_be_implictly_casted_to_a_decider()
        {
            Func<Exception,Directive> decider = new DeciderBuilder(Directive.Escalate);

            decider(new Exception()).ShouldBe(Directive.Escalate);           
        }

        //  Exception
        //     |
        //     +-- ExceptionA
        //     |       |
        //     |      ExceptionASub
        //     |       |
        //     |      ExceptionASubSub
        //     |
        //     +-- ExceptionB
        //             |
        //            ExceptionBSub
        private class ExceptionA : Exception { }
        private class ExceptionASub : ExceptionA { }
        private class ExceptionASubSub : ExceptionASub { }
        private class ExceptionB : Exception { }
        private class ExceptionBSub : ExceptionB { }
    }
}