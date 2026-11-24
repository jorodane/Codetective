using System;
using System.Linq;
using Codetective.Operations;
using NUnit.Framework;

namespace Codetective.Tests.Operations
{
    public sealed class ContentLocalizationTests
    {
        private static TextTranslation Row(string key, string language, string text) => new TextTranslation { key = key, language = language, text = text };
        private static KernelRuntime World(string englishName = "Victor Hale")
        {
            var r = new KernelRuntime { Language = CommandLocale.Korean };
            r.Texts.Install(new[] { Row("person.name", "ko", "빅터 헤일"), Row("person.name", "en", englishName),
                Row("person.description", "ko", "행사 참석자"), Row("person.description", "en", "An event attendee"),
                Row("person.job", "ko", "회사 대표"), Row("person.job", "en", "Company director"),
                Row("claim", "ko", "행사장에서 보았습니다."), Row("claim", "en", "I saw him at the event."),
                Row("witness", "ko", "목격자"), Row("witness", "en", "Witness") });
            Entity p = r.CreateInstance("Person", "victor", "원문 이름", false); p.LabelKey = "person.name";
            p.Names = new[] { new LocalizedCommandName(CommandLocale.Korean, "인물01") };
            p.SetValue("description", Datum.Text("원문 설명", "person.description")); p.SetValue("occupation", Datum.Text("원문 직업", "person.job"));
            r.Register(p); r.CollectEvidence(r.Reference(p)); return r;
        }
        [Test] public void ChangingDisplayLanguageDoesNotChangeCommandsAliasesOrProtocolSources()
        {
            KernelRuntime r = World(); r.Execute("피해자 = victor"); const string source = "protocol relay() { return 피해자; }";
            Assert.That(r.SaveProtocol(source).Success, Is.True); string[] commands = r.Complete("").Select(c => c.Replacement).ToArray();
            Assert.That(r.ViewData("victor").Name, Is.EqualTo("빅터 헤일")); r.DisplayLanguage = "en-GB";
            Assert.That(r.ViewData("victor").Name, Is.EqualTo("Victor Hale")); Assert.That(r.Language, Is.EqualTo(CommandLocale.Korean));
            Assert.That(r.Complete("").Select(c => c.Replacement), Is.EqualTo(commands));
            Assert.That(r.Data.Keys, Does.Contain("피해자")); Assert.That(r.Protocols["relay"].Source, Is.EqualTo(source));
            Assert.That(r.Execute("relay()").Value.id, Is.EqualTo("victor")); Assert.That(r.Entity("victor").Label, Is.EqualTo("원문 이름"));
        }
        [Test] public void ProfileEvidenceViewerAndCommandResultsResolveAtDisplayTime()
        {
            KernelRuntime r = World(); r.DisplayLanguage = "en";
            Assert.That(r.ProfileCard("victor").DisplayName, Is.EqualTo("Victor Hale"));
            Assert.That(r.ProfileCard("victor").Occupation, Is.EqualTo("Company director"));
            Assert.That(r.Evidence.Single(e => e.Id == "victor").Name, Is.EqualTo("Victor Hale"));
            Assert.That(r.Evidence.Single(e => e.Id == "victor").Description, Is.EqualTo("An event attendee"));
            Assert.That(r.ViewData("victor").Text(), Does.Contain("An event attendee").And.Contain("Company director").And.Not.Contain("원문"));
            Assert.That(r.Execute("victor.name").Message, Is.EqualTo("Victor Hale"));
            Assert.That(r.Execute("victor.name").Value.text, Is.EqualTo("원문 이름"));
        }
        [Test] public void SavedReferencesAndObservationsUseNewCatalogTranslationsAfterLoading()
        {
            KernelRuntime r = World(); r.Execute("피해자 = victor");
            r.Observe(new ObservationDefinition { subjectId = "victor", field = "description", value = Datum.Text("원문 설명", "person.description"),
                statement = "원문 진술", statementKey = "claim", source = "원문 출처", sourceKey = "witness" });
            KernelSnapshot saved = r.Capture(); KernelRuntime loaded = World("Updated translation"); loaded.DisplayLanguage = "en"; loaded.Restore(saved);
            Assert.That(loaded.ViewData("victor").Name, Is.EqualTo("Updated translation"));
            Assert.That(loaded.Format(loaded.Data["피해자"].value), Is.EqualTo("Updated translation"));
            Assert.That(loaded.ViewData("victor").Text(), Does.Contain("I saw him at the event.").And.Contain("Witness"));
            EvidenceEntry claim = loaded.Evidence.First(e => e.Id.StartsWith("observation_", StringComparison.Ordinal));
            Assert.That(claim.Name, Does.Contain("Witness")); Assert.That(loaded.ViewData(claim.Id).Text(), Does.Contain("I saw him at the event.").And.Not.Contain("원문 진술"));
        }
        [Test] public void NestedArraysAndDictionariesKeepTextKeysAcrossCopiesAndSaves()
        {
            KernelRuntime r = World(); r.DisplayLanguage = "en";
            Datum value = Datum.Dict(Datum.Text("key"), Datum.List(r.Reference(r.Entity("victor")), Datum.Text("원문 설명", "person.description")));
            r.Store("items", value); KernelSnapshot saved = r.Capture(); KernelRuntime loaded = World(); loaded.DisplayLanguage = "en"; loaded.Restore(saved);
            Assert.That(loaded.Format(loaded.Data["items"].value), Does.Contain("Victor Hale").And.Contain("An event attendee"));
            Assert.That(loaded.Data["items"].value.Elements[1].Elements[1].textKey, Is.EqualTo("person.description"));
        }
        [Test] public void TranslationFallbackUsesParentLocaleThenSourceLanguageThenLiteral()
        {
            var t = new ContentTextCatalog(); t.Install(new[] { Row("key", "en", "English"), Row("key", "ko", "한국어"), Row("blank", "en", "") });
            Assert.That(t.Resolve("key", "source", "EN_us"), Is.EqualTo("English"));
            Assert.That(t.Resolve("key", "source", "ja"), Is.EqualTo("한국어"));
            Assert.That(t.Resolve("missing", "source", "en"), Is.EqualTo("source"));
            Assert.That(t.Resolve("missing", "", "en"), Is.EqualTo("missing"));
            Assert.That(t.Resolve("blank", "source", "en"), Is.Empty);
        }
        [Test] public void OldSavesCanRecoverAuthoredNameKeysWithoutRewritingNewUserStrings()
        {
            KernelRuntime r = World(); KernelSnapshot old = r.Capture(); old.contentTextVersion = 0;
            foreach (EntitySnapshot entity in old.entities) { entity.labelKey = null; foreach (DataSlot slot in entity.values) slot.value.textKey = null; }
            foreach (ObservationRecord record in old.observations) record.value.textKey = null;
            KernelRuntime loaded = World(); loaded.DisplayLanguage = "en"; loaded.Restore(old);
            Assert.That(loaded.ViewData("victor").Name, Is.EqualTo("Victor Hale"));
            Assert.That(loaded.ProfileCard("victor").Occupation, Is.EqualTo("Company director"));
            r.Observe(new ObservationDefinition { subjectId = "victor", field = "occupation", value = Datum.Text("원문 직업") });
            loaded.Restore(r.Capture()); Assert.That(loaded.ProfileCard("victor").Occupation, Is.EqualTo("원문 직업"));
        }
        [Test] public void InvalidAndDuplicateTranslationsAreRejectedWithoutPartialInstall()
        {
            var t = new ContentTextCatalog(); t.Install(new[] { Row("key", "en", "original") });
            Assert.That(() => t.Install(new[] { Row("new", "ko", "new"), Row("key", "EN", "duplicate") }), Throws.TypeOf<CommandException>());
            Assert.That(t.Resolve("new", "fallback", "ko"), Is.EqualTo("fallback"));
            Assert.That(() => t.Install(new[] { Row("x", "en?", "x") }), Throws.TypeOf<CommandException>());
        }
        [Test] public void OldAnonymousNamesDoNotInheritTheAuthoredRealNameTranslation()
        {
            KernelSnapshot old = World().Capture(); old.contentTextVersion = 0;
            EntitySnapshot person = old.entities.Single(e => e.id == "victor"); person.label = "Anonymous"; person.labelKey = "";
            foreach (ObservationRecord record in old.observations.Where(o => o.subjectId == "victor" && o.field == "name")) record.value = Datum.Text("Anonymous");
            KernelRuntime loaded = World(); loaded.DisplayLanguage = "en"; loaded.Restore(old);
            Assert.That(loaded.ViewData("victor").Name, Is.EqualTo("Anonymous"));
        }
        [Test] public void LanguageAndFormattingDoNotExposeUnknownOrRestrictedWorldValues()
        {
            KernelRuntime r = World(); Entity p = r.Entity("victor");
            p.AddVariable(new VariableDefinition { Code = "secret", Kind = DataKind.Text, DefaultValue = Datum.Text("secret", "person.description"), Access = new AccessRule { PlayerAccess = PlayerAccess.Internal } });
            p.SetValue("description", Datum.Text("unreported change", "claim")); r.DisplayLanguage = "en";
            Assert.That(r.ViewData("victor").Text(), Does.Contain("An event attendee").And.Not.Contain("I saw him"));
            Assert.That(r.ViewData("victor").Fields.Any(f => f.Code == "secret"), Is.False);
            Assert.That(r.Format(Datum.Text("피해자")), Is.EqualTo("피해자")); // User text is never looked up as a key.
        }
    }
}
