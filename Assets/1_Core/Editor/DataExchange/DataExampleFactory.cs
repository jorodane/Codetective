using System;
using Codetective.Gameplay;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.DataExchange
{
    /// <summary>A small authoring example, deliberately separate from the playable scenario.</summary>
    public static class DataExampleFactory
    {
        public static WorldDefinition Create()
        {
            T Make<T>(string id, string label) where T : WorldDataAsset
            { T a = ScriptableObject.CreateInstance<T>(); a.name = id; a.instanceId = id; a.displayName = label; a.exposeRoot = false; return a; }
            var start = Make<TimeAsset>("example_start", "업무 시작"); start.year = 1994; start.month = 6; start.day = 3;
            var time = Make<TimeAsset>("example_time", "1994-06-02 14:32"); time.year = 1994; time.month = 6; time.day = 2; time.hour = 14; time.minute = 32;
            var concept = Make<ConceptAsset>("example_attendance", "행사 참석"); concept.description = "행사 참석 여부에 관한 조사 소재";
            var keyword = Make<KeywordAsset>("example_keyword", "참석 여부"); keyword.topic = concept;
            var hall = Make<PlaceAsset>("example_hall", "공개행사장"); var warehouse = Make<PlaceAsset>("example_warehouse", "창고");
            hall.connections = new[] { warehouse }; warehouse.connections = new[] { hall };
            var person = Make<PersonAsset>("example_victor", "Victor Hale"); person.age = 48; person.occupation = "Meridian Profile Services 대표";
            person.displayNameKey = "sample.victor.name";
            person.description = "실종된 기업 대표";
            person.textKeys = new[] { new FieldTextKey { field = "description", key = "sample.victor.description" }, new FieldTextKey { field = "occupation", key = "sample.victor.occupation" } };
            var witness = Make<PersonAsset>("example_rachel", "Rachel Kim"); witness.occupation = "행사 담당 직원"; witness.topics = new[] { keyword };
            person.fields = new[] { new FieldDefinition { code = "currentPlace", label = "현재 위치", kind = DataKind.Place, typeName = "Place" } };
            person.initialValues = new[] { new InitialFieldDefinition { field = "currentPlace", reference = warehouse } };
            var relation = Make<RelationshipAsset>("example_relation", "명찰 전달"); relation.category = "목격"; relation.description = "명찰을 직접 전달했다.";
            witness.relations = new[] { new RelationLink { target = person, relations = new[] { relation } } };
            var photo = Make<PhotoAsset>("example_photo", "행사 현장 사진"); photo.takenAt = time; photo.description = "Image에 프로젝트의 Sprite를 지정하세요.";
            person.portrait = photo;
            var badge = Make<ItemAsset>("example_badge", "행사 명찰"); badge.owner = person; person.inventory = new[] { badge };
            var tool = Make<ToolAsset>("example_tool", "확대경"); tool.description = "도구별 실제 동작은 Functions에서 작성합니다.";
            var arrival = Make<EventAsset>("example_arrival", "Victor Hale 행사 입장"); arrival.occurredAt = time; arrival.place = hall; arrival.scenePhoto = photo;
            arrival.subjects = new[] { new RelationLink { target = person, relations = new[] { relation } } }; arrival.content = "Victor가 행사장에 실제로 입장했다.";
            var incident = Make<EventAsset>("example_event", "행사 기록 사건"); incident.subevents = new[] { arrival }; incident.occurredAt = time;
            person.history = new[] { arrival };
            var statement = Make<StatementAsset>("example_statement", "Rachel의 진술"); statement.speaker = witness; statement.topic = concept;
            statement.spokenAt = time; statement.content = "제가 Hale 대표님께 직접 명찰을 전달했어요."; statement.subjects = new PhysicalAsset[] { person, hall };
            var organization = Make<OrganizationAsset>("example_organization", "행사 운영 조직"); organization.representative = witness;
            organization.members = new[] { new OrganizationMember { person = witness, rank = "담당 직원" } };
            organization.places = new[] { new OrganizationPlace { place = hall, purpose = "공개행사" } };
            var viewer = ScriptableObject.CreateInstance<DataViewerAsset>(); viewer.name = "ExamplePersonViewer"; viewer.viewerId = "example_person_viewer";
            viewer.layout = DataViewerKind.Person; viewer.title = "인물 프로필"; viewer.fields = new[] { "age", "occupation", "currentPlace", "history", "relations", "topics" }; person.viewer = viewer;
            viewer.titleKey = "sample.viewer.person";
            var knowledge = ScriptableObject.CreateInstance<CurrentInformationAsset>(); knowledge.name = "ExampleInformation"; knowledge.informationId = "example_information";
            knowledge.discoveries = new[] { new InitialDiscovery { target = person }, new InitialDiscovery { target = witness },
                new InitialDiscovery { target = statement, observeFields = true }, new InitialDiscovery { target = time, observeFields = true },
                new InitialDiscovery { target = organization, observeFields = true }, new InitialDiscovery { target = tool, observeFields = true }, new InitialDiscovery { target = keyword, observeFields = true } };
            knowledge.entries = new[] {
                new InformationEntry { subject = person, field = "age", readWorldField = true },
                new InformationEntry { subject = person, field = "occupation", readWorldField = true },
                new InformationEntry { subject = person, field = "currentPlace", referenceValue = hall, source = "Rachel 진술", sourceObject = witness,
                    sourceKey = "sample.source.witness", statementKey = "sample.claim.attended", statement = "행사장에서 Victor를 보았습니다.", kind = InformationKind.Testimony, observedAt = new GameDateTime(1994, 6, 2, 14, 32) },
                new InformationEntry { subject = person, field = "currentPlace", value = Datum.Empty, source = "최종 행사 기록",
                    sourceKey = "sample.source.official", statementKey = "sample.claim.absent", statement = "Victor Hale / 개인 사정으로 불참", kind = InformationKind.Observation, observedAt = new GameDateTime(1994, 6, 2, 18, 0) } };
            knowledge.inferences = new[] { new InferredInformation { id = "example_hypothesis", name = "참석 기준에 대한 가설", content = "공식 참석 기록이 실제 입장 여부와 다를 수 있다." } };
            knowledge.references = new[] { new InitialReference { name = "피해자", target = person }, new InitialReference { name = "참석여부", target = keyword },
                new InitialReference { name = "가설", informationId = "example_hypothesis" } };
            var world = ScriptableObject.CreateInstance<WorldDefinition>(); world.name = "ExampleWorld"; world.worldId = "example_world"; world.includeClinicDefaults = false;
            world.currentTime = start; world.objects = new EntityInstanceAsset[] { person, witness, organization, tool, incident, statement };
            world.occurredEvents = new[] { incident }; world.initialInformation = knowledge;
            var texts = ScriptableObject.CreateInstance<ContentTextTableAsset>(); texts.name = "ExampleTexts"; texts.tableId = "example_texts";
            TextTranslation T(string key, string language, string text) => new TextTranslation { key = key, language = language, text = text };
            texts.entries = new[] {
                T("sample.victor.name", "ko", "빅터 헤일"), T("sample.victor.name", "en", "Victor Hale"),
                T("sample.victor.description", "ko", "실종된 기업 대표"), T("sample.victor.description", "en", "A missing company director"),
                T("sample.victor.occupation", "ko", "Meridian Profile Services 대표"), T("sample.victor.occupation", "en", "Director of Meridian Profile Services"),
                T("sample.viewer.person", "ko", "인물 프로필"), T("sample.viewer.person", "en", "Person profile"),
                T("sample.source.witness", "ko", "Rachel 진술"), T("sample.source.witness", "en", "Rachel's testimony"),
                T("sample.source.official", "ko", "최종 행사 기록"), T("sample.source.official", "en", "Final attendance record"),
                T("sample.claim.attended", "ko", "행사장에서 Victor를 보았습니다."), T("sample.claim.attended", "en", "I saw Victor at the event."),
                T("sample.claim.absent", "ko", "Victor Hale / 개인 사정으로 불참"), T("sample.claim.absent", "en", "Victor Hale / Absent for personal reasons") };
            world.textTables = new[] { texts };
            return world;
        }
    }
}
