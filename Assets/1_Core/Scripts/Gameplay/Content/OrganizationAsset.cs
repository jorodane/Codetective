using System;
using System.Collections.Generic;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Data/Organization", fileName = "Organization")]
    public sealed class OrganizationAsset : PhysicalAsset
    {
        public override string BuiltinSchemaId => "Organization";
        public PersonAsset representative;
        public OrganizationMember[] members = Array.Empty<OrganizationMember>();
        public OrganizationPlace[] places = Array.Empty<OrganizationPlace>();
        protected override void Write(DataAssetBuilder b)
        {
            base.Write(b); b.Reference("representative", representative);
            var values = new List<Datum>();
            foreach (OrganizationMember member in members)
            { if (member == null || member.person == null) throw new CommandException("조직 구성원이 비어 있습니다."); values.Add(b.Ref(member.person)); values.Add(Datum.Text(member.rank ?? "")); }
            b.Set("members", Datum.Dict(values.ToArray())); values.Clear();
            foreach (OrganizationPlace entry in places)
            { if (entry == null || entry.place == null) throw new CommandException("조직 장소가 비어 있습니다."); values.Add(b.Ref(entry.place)); values.Add(Datum.Text(entry.purpose ?? "")); }
            b.Set("places", Datum.Dict(values.ToArray()));
        }
    }
}
