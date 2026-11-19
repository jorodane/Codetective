using Codetective.Gameplay;
using Codetective.Operations;
using NUnit.Framework;
using UnityEngine;

namespace Codetective.Tests.EntityAuthoring
{
    public sealed class ProfileCatalogTests
    {
        [Test]
        public void InstanceIdentityTakesPrecedenceAndDuplicateProfileBindingsAreRejected()
        {
            var catalog = ScriptableObject.CreateInstance<ProfileCatalog>();
            var person = ScriptableObject.CreateInstance<CharacterProfileAsset>(); var tool = ScriptableObject.CreateInstance<ToolProfileAsset>();
            var entity = ScriptableObject.CreateInstance<EntityInstanceAsset>();
            try
            {
                entity.instanceId = "same"; person.worldId = "ignored"; person.entity = entity; tool.worldId = "same";
                catalog.characters = new[] { person }; catalog.tools = new[] { tool };
                Assert.That(person.Id, Is.EqualTo("same")); Assert.That(() => catalog.Validate(), Throws.TypeOf<CommandException>());
                tool.worldId = "tool"; Assert.That(() => catalog.Validate(), Throws.Nothing);
            }
            finally { Object.DestroyImmediate(catalog); Object.DestroyImmediate(person); Object.DestroyImmediate(tool); Object.DestroyImmediate(entity); }
        }
        [Test]
        public void ProfileDataDoesNotGrantOwnershipOrOverrideRuntimeAccessRules()
        {
            var catalog = ScriptableObject.CreateInstance<ProfileCatalog>(); var tool = ScriptableObject.CreateInstance<ToolProfileAsset>();
            try
            {
                tool.worldId = "toolkit"; tool.description = "등록된 도구 설명"; catalog.tools = new[] { tool };
                KernelRuntime runtime = ClinicScenario.Create(); runtime.Store("tool", runtime.Reference(runtime.Entity("toolkit")));
                catalog.ApplyDescriptions(runtime); Assert.That(runtime.Evidence, Is.Empty); Assert.That(runtime.Execute("tool.description").Success, Is.False);
                runtime.Facts.Add("coffee_received"); Assert.That(runtime.Evidence, Is.Empty);
                runtime.CollectEvidence(runtime.Reference(runtime.Entity("toolkit")), "수령 보고"); Assert.That(runtime.Evidence.Count, Is.EqualTo(1));
                Assert.That(runtime.Execute("tool.description").Value.text, Is.EqualTo("등록된 도구 설명"));
                runtime.Entity("toolkit").SetVariableAccess("description", PlayerAccess.Internal); catalog.ApplyDescriptions(runtime);
                Assert.That(runtime.Execute("tool.description").Success, Is.False); Assert.That(runtime.Evidence[0].Description, Does.Not.Contain("등록된 도구 설명"));
            }
            finally { Object.DestroyImmediate(catalog); Object.DestroyImmediate(tool); }
        }
        [Test]
        public void AScenarioInstallsAnInstanceReferencedOnlyByACharacterProfile()
        {
            var scenario = ScriptableObject.CreateInstance<KernelScenarioDefinition>(); var catalog = ScriptableObject.CreateInstance<ProfileCatalog>();
            var person = ScriptableObject.CreateInstance<CharacterProfileAsset>(); var instance = ScriptableObject.CreateInstance<EntityInstanceAsset>();
            var schema = ScriptableObject.CreateInstance<EntitySchemaAsset>();
            try
            {
                schema.schemaId = "NewVisitor"; schema.builtinParent = "Person";
                instance.instanceId = "newVisitor"; instance.displayName = "새 상담자"; instance.schema = schema; instance.exposeRoot = false;
                person.entity = instance; person.description = "프로필의 설명"; catalog.characters = new[] { person }; scenario.profiles = catalog;
                KernelRuntime runtime = scenario.CreateRuntime(); Assert.That(runtime.Entity("newVisitor"), Is.Not.Null);
                Assert.That(runtime.Entity("newVisitor").GetValue("description").text, Is.EqualTo("프로필의 설명"));
                Assert.That(runtime.Execute("newVisitor").Success, Is.True);
            }
            finally { Object.DestroyImmediate(scenario); Object.DestroyImmediate(catalog); Object.DestroyImmediate(person); Object.DestroyImmediate(instance); Object.DestroyImmediate(schema); }
        }
    }
}
