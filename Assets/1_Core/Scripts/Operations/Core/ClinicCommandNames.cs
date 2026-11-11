using System;
using System.Collections.Generic;

namespace Codetective.Operations
{
    internal static class ClinicCommandNames
    {
        private static readonly Dictionary<string, string[]> Translations = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "hq", new[] { "지휘소", "Headquarters" } },
            { "hq.requestCoffee", new[] { "커피요청", "Request coffee" } },
            { "hq.coffee", new[] { "내커피", "Delivered coffee" } },
            { "hq.checkComms", new[] { "통신확인", "Check communications" } },
            { "hq.requestSupplies", new[] { "물품요청", "Request supplies" } },
            { "hq.lockDoor", new[] { "문잠금", "Lock the clinic door" } },
            { "hq.turnOffLights", new[] { "조명끄기", "Turn off the lights" } },
            { "hq.callPolice", new[] { "경찰연결", "Contact the police" } },
            { "hq_door", new[] { "문", "Clinic door", "door" } },
            { "hq_door.lock", new[] { "잠근다", "Lock the door" } },
            { "hq_lights", new[] { "조명", "Clinic lights", "lights" } },
            { "hq_lights.turnOff", new[] { "끈다", "Turn off the lights" } },
            { "archive", new[] { "자료실", "Field archive" } },
            { "archive.inspect", new[] { "조사", "Inspect the archive" } },
            { "archive.analyze", new[] { "분석", "Analyze a document" } },
            { "client", new[] { "상담손님", "Counseling client" } },
            { "client.identity", new[] { "프로필", "Client profile" } },
            { "client.showEvidence", new[] { "증거제시", "Present analysis to the client" } },
            { "coffee", new[] { "커피", "Kernel's coffee" } },
            { "coffee.inspect", new[] { "확인", "Inspect the coffee" } },
            { "thread.status", new[] { "상태", "Current status" } },
            { "thread.report", new[] { "보고", "Latest report" } },
            { "thread.run", new[] { "프로토콜실행", "Assign a protocol" } },
            { "thread.resume", new[] { "재개", "Resume the operation" } },
            { "thread.recall", new[] { "복귀", "Recall the operative" } }
        };

        public static void Apply(Entity entity)
        {
            if (entity.Kind == DataKind.Thread)
                entity.Names = Names("스레드" + entity.Id.Substring(6), entity.Label, entity.Id, "Anonymous operative");
            else if (Translations.TryGetValue(entity.Id, out string[] root))
                entity.Names = Names(root[0], entity.Label, root.Length > 2 ? root[2] : entity.Id, root[1]);
            foreach (Operation action in entity.Operations)
            {
                string key = (entity.Kind == DataKind.Thread ? "thread" : entity.Id) + "." + action.Code;
                if (Translations.TryGetValue(key, out string[] name))
                    action.Names = Names(name[0], action.Label, action.Code, name[1]);
            }
            foreach (Entity child in entity.Children.Values) Apply(child);
        }

        private static LocalizedCommandName[] Names(string korean, string koreanLabel, string english, string englishLabel) =>
            new[] { new LocalizedCommandName(CommandLocale.Korean, korean, koreanLabel), new LocalizedCommandName(CommandLocale.English, english, englishLabel) };
    }
}
