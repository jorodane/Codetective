using System;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Data/Person", fileName = "Person")]
    public sealed class PersonAsset : PhysicalAsset
    {
        public override string BuiltinSchemaId => "Person";
        public string gender, occupation;
        [Min(0)] public int age;
        public ItemAsset[] inventory = Array.Empty<ItemAsset>();
        public KeywordAsset[] topics = Array.Empty<KeywordAsset>();
        protected override void Write(DataAssetBuilder b)
        { if (age < 0) throw new CommandException("나이는 0 이상이어야 합니다: " + instanceId); base.Write(b); b.Set("gender", Datum.Text(gender ?? "")); b.Set("age", Datum.Number(age)); b.Set("occupation", Datum.Text(occupation ?? "")); b.List("inventory", inventory); b.List("topics", topics); }
    }
}
