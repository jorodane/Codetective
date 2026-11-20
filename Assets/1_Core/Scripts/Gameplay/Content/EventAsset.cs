using System;
using System.Collections.Generic;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Data/Event", fileName = "Event")]
    public sealed class EventAsset : ConceptAsset
    {
        public override string BuiltinSchemaId => "Event";
        public RelationLink[] subjects = Array.Empty<RelationLink>();
        public PhotoAsset scenePhoto;
        public TimeAsset occurredAt;
        public PlaceAsset place;
        public EventAsset[] subevents = Array.Empty<EventAsset>();
        [TextArea] public string content, note;
        protected override void Write(DataAssetBuilder b)
        {
            ValidateChildren(new HashSet<EventAsset>(), new HashSet<EventAsset>());
            base.Write(b); b.Relations("subjects", subjects); b.Reference("photo", scenePhoto); b.Reference("time", occurredAt); b.Reference("place", place); b.List("subevents", subevents); b.Set("content", Datum.Text(content ?? "")); b.Set("note", Datum.Text(note ?? ""));
        }
        private void ValidateChildren(HashSet<EventAsset> active, HashSet<EventAsset> complete)
        {
            if (complete.Contains(this)) return;
            if (!active.Add(this) || active.Count > 64) throw new CommandException("세부사건이 순환하거나 깊이(64)를 초과했습니다: " + instanceId);
            foreach (EventAsset child in subevents ?? Array.Empty<EventAsset>())
            { if (child == null) throw new CommandException("세부사건에 빈 참조가 있습니다: " + instanceId); child.ValidateChildren(active, complete); }
            active.Remove(this); complete.Add(this);
        }
    }
}
