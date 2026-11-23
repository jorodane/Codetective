using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    [Serializable]
    public sealed class ConsultationEpisode
    {
        public string id;
        public ConsultationLine[] lines = Array.Empty<ConsultationLine>();
        public ConsultationEpisode Copy() => new ConsultationEpisode { id = id, lines = lines.Select(line => line.Copy()).ToArray() };
    }

    public sealed partial class KernelRuntime
    {
        private readonly Dictionary<string, ConsultationEpisode[]> consultationPools = new Dictionary<string, ConsultationEpisode[]>(StringComparer.Ordinal);
        private int consultationSeed = Guid.NewGuid().GetHashCode() | 1;
        private ConsultationLine[] activeEpisodeLines;

        public void RegisterConsultationPool(string id, ConsultationEpisode[] episodes)
        {
            if (!CommandParser.IsIdentifier(id) || consultationPools.ContainsKey(id) || episodes == null || episodes.Length == 0 ||
                episodes.Any(episode => episode == null || !CommandParser.IsIdentifier(episode.id) || episode.lines == null || episode.lines.Length == 0 ||
                    episode.lines.Any(line => line == null || string.IsNullOrWhiteSpace(line.sentence) ||
                        !string.IsNullOrWhiteSpace(line.keyword) || !string.IsNullOrWhiteSpace(line.fact) ||
                        !HasValidConsultationResponses(line))) ||
                episodes.Select(episode => episode.id).Distinct(StringComparer.Ordinal).Count() != episodes.Length)
                throw new CommandException("일상 상담의 에피소드와 응답을 확인하세요: " + id);
            consultationPools.Add(id, episodes.Select(episode => episode.Copy()).ToArray());
        }

        private static bool HasValidConsultationResponses(ConsultationLine line) =>
            line.responses == null || (line.responses.Length <= 2 && !line.responses.Any(string.IsNullOrWhiteSpace));

        private void SelectVisitEpisode(AppointmentProgress visit, PatientAppointment definition)
        {
            activeEpisodeLines = null;
            if (string.IsNullOrEmpty(definition.episodePoolId)) return;
            ConsultationEpisode[] episodes = consultationPools[definition.episodePoolId];
            int seed = consultationSeed;
            foreach (char character in visit.id) seed = unchecked(seed * 31 + character);
            ConsultationEpisode selected = EpisodeFor(visit) ?? episodes[new Random(unchecked(seed * 31 + Day)).Next(episodes.Length)];
            visit.episodeId = selected.id;
            visit.lineCount = selected.lines.Length;
            BindVisitEpisode(visit, selected);
        }

        private void BindVisitEpisode(AppointmentProgress visit, ConsultationEpisode episode)
        {
            activeEpisodeLines = episode.lines.Select(line => line.Copy()).ToArray();
            foreach (ConsultationLine line in activeEpisodeLines)
            {
                line.profileId = visit.profileId;
                line.speaker = visit.patientName;
            }
        }

        private ConsultationEpisode EpisodeFor(AppointmentProgress visit)
        {
            if (string.IsNullOrEmpty(visit.episodeId) || !appointmentDefinitions.TryGetValue(visit.id, out PatientAppointment definition) ||
                string.IsNullOrEmpty(definition.episodePoolId) || !consultationPools.TryGetValue(definition.episodePoolId, out ConsultationEpisode[] episodes))
                return null;
            return episodes.FirstOrDefault(episode => episode.id == visit.episodeId);
        }
    }
}
