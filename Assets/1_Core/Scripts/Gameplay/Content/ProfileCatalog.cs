using System;
using System.Collections.Generic;
using System.Linq;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Profiles/Catalog", fileName = "ProfileCatalog")]
    public sealed class ProfileCatalog : ScriptableObject
    {
        public CharacterProfileAsset[] characters = Array.Empty<CharacterProfileAsset>();
        public ToolProfileAsset[] tools = Array.Empty<ToolProfileAsset>();
        public IEnumerable<VisualProfileAsset> All => (characters ?? Array.Empty<CharacterProfileAsset>()).Cast<VisualProfileAsset>()
            .Concat(tools ?? Array.Empty<ToolProfileAsset>()).Where(p => p != null);
        public CharacterProfileAsset Character(string id) => characters?.FirstOrDefault(p => p != null && p.Id == id);
        public VisualProfileAsset Find(string id) => All.FirstOrDefault(p => p.Id == id);
        public void Validate()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (VisualProfileAsset profile in All)
                if (string.IsNullOrWhiteSpace(profile.Id) || !ids.Add(profile.Id)) throw new CommandException("프로필 ID가 비어 있거나 중복됩니다: " + profile.Id);
        }
        public void ApplyDescriptions(KernelRuntime runtime)
        {
            Validate();
            foreach (VisualProfileAsset profile in All)
            {
                Entity target = runtime.Entity(profile.Id);
                if (target != null && !string.IsNullOrWhiteSpace(profile.description) && target.FindVariable("description") != null)
                    target.SetValue("description", Datum.Text(profile.description));
            }
        }
    }
}
