using System;
using System.Collections.Generic;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    /// <summary>The initial authored example; the runtime conversation player has no onboarding-specific branches.</summary>
    public static class TerminalOnboarding
    {
        public static TerminalConversationAsset Create()
        {
            var rows = new List<TextTranslation>();
            void Text(string key, string ko, string en)
            { rows.Add(new TextTranslation { key = key, language = "ko", text = ko }); rows.Add(new TextTranslation { key = key, language = "en", text = en }); }
            TerminalDialogueLine Line(string key, string ko, string en)
            { Text("onboarding." + key, ko, en); return new TerminalDialogueLine { textKey = "onboarding." + key, text = ko }; }
            Text("onboarding.speaker", "현장지원과", "Field Support");
            Text("terminal.dialogue.receiving", "수신 중 · 안내가 끝나면 입력할 수 있습니다.", "Receiving · wait until the message finishes.");
            Text("terminal.dialogue.command", "안내 명령", "Requested command");
            Text("terminal.dialogue.standby", "작전망 연결 대기", "Operations network standby");
            var steps = new[] {
                new TerminalConversationStep { id = "help", lines = new[] {
                    Line("welcome", "Kernel을 찾으셨군요.", "You found Kernel."),
                    Line("connection", "현장지원과입니다. 메일은 확인하셨겠지요. 먼저 작전망의 기본 사용법부터 확인하겠습니다.", "Field Support here. You have read the mail. Let's check the basics of the operations network."),
                    Line("listen", "안내가 수신되는 동안에는 명령을 입력할 수 없습니다. 안내가 끝나면 다음 명령을 입력해 보십시오:\n{command}", "You cannot enter commands while a message is being received. When it finishes, enter {command}.") },
                    goal = new TerminalCommandGoal { kind = TerminalGoalKind.Help } },
                new TerminalConversationStep { id = "allocate", lines = new[] {
                    Line("permissions", "잘하셨습니다. 권한 목록은 현재 사용할 수 있는 정보와 행동을 보여줍니다.", "Good. The permissions list shows the information and actions currently available to you."),
                    Line("allocate", "이번에는 빈 치환문을 등록하겠습니다.\n입력: {command}\n이번 연습에 사용할 이름은 {slot}입니다.", "Let's register an empty substitution. Enter {command}. We will use the name {slot} for this exercise.") },
                    goal = new TerminalCommandGoal { kind = TerminalGoalKind.Allocate } },
                new TerminalConversationStep { id = "reference", lines = new[] {
                    Line("reference", "다음 명령으로 {slot}에 지휘소를 연결하십시오. 등호 오른쪽에 치환할 대상을 적습니다.\n{command}", "Enter {command} to bind {slot} to headquarters. Write the target on the right of the equals sign.") },
                    goal = new TerminalCommandGoal { kind = TerminalGoalKind.BindReference, targetId = "hq" } },
                new TerminalConversationStep { id = "invoke", lines = new[] {
                    Line("substitution", "이제 지휘소를 가리키는 이름은 {slot}입니다.", "Now you can use {slot} to refer to headquarters."),
                    Line("dot", "대상 뒤에 마침표를 입력하면 권한 목록이 나옵니다. Tab이나 Enter로 후보를 확정하십시오. 후보 이름을 쓰기 시작하면 Space로도 확정할 수 있습니다. 다음 Enter로 명령을 보냅니다.", "Type a dot after a target to see its permissions. Accept a suggestion with Tab or Enter. Space also accepts after you start typing its name. Press Enter again to send the command."),
                    Line("invoke", "연결 상태를 확인하겠습니다. 다음 명령을 입력하십시오:\n{command}", "Let's check the connection. Enter {command}.") },
                    goal = new TerminalCommandGoal { kind = TerminalGoalKind.Invoke, targetId = "hq", operation = "checkComms" } },
                new TerminalConversationStep { id = "release", lines = new[] {
                    Line("release", "통신 확인. 마지막으로 다음 명령으로 연습용 치환문을 폐기하겠습니다:\n{command}", "Connection confirmed. Finally, discard the exercise substitution by entering {command}.") },
                    goal = new TerminalCommandGoal { kind = TerminalGoalKind.Free } },
                new TerminalConversationStep { id = "finish", lines = new[] {
                    Line("world", "치환문을 폐기해도 실제 지휘소는 사라지지 않습니다. 대상을 부르는 규칙만 정리한 것입니다.", "Discarding a substitution does not remove headquarters. You have only removed a rule for referring to it."),
                    Line("coffee", "지휘소의 권한 목록에서 커피 요청도 찾아보십시오. 명령은 화면 밖의 실제 행동으로 이어집니다.", "You can also find a coffee request in headquarters' permissions. Commands lead to real actions beyond the screen."),
                    Line("finish", "기본 연결 확인을 마쳤습니다. 남은 임무 안내와 수락 여부는 메일에서 확인하십시오. 이제 귀하의 명령을 기다리겠습니다.", "Basic checks are complete. Review the assignment and its acceptance in Mail. We will now await your commands.") } }
            };
            var asset = ScriptableObject.CreateInstance<TerminalConversationAsset>(); asset.name = "KernelOnboarding";
            asset.conversation = new TerminalConversationDefinition { id = "kernel-onboarding", requiredMailRead = "government-intro", firstDay = 1, lastDay = 1,
                koreanVariableBase = "연습",
                speaker = "현장지원과", speakerKey = "onboarding.speaker", pauseWorld = true, replacesStartupGuide = true, skipIfMailAlreadyReadOnExistingSave = true, angryAfter = 3, steps = steps,
                condition = new StoryCondition { triggersOff = new[] { "terminal.onboarding.complete" } },
                forgivingReplies = new[] {
                    Line("detour1", "그 명령도 유효합니다만, 지금은 다음 명령을 확인하는 중입니다:\n{command}\n계속해 볼까요?", "That command is valid, but we're checking {command} right now. Shall we continue?"),
                    Line("detour2", "호기심은 좋습니다. 순서만 지켜 주십시오. 지금 요청한 명령은 {command}입니다.", "Curiosity is useful. Please follow the sequence. The requested command is {command}.") },
                angryReplies = new[] {
                    Line("angry1", "Kernel. 지금은 장난칠 때가 아닙니다.\n{command}\n지시를 이행하십시오.", "Kernel. This is not the time for games.\n{command}\nFollow the instruction."),
                    Line("angry2", "같은 설명을 반복하게 하지 마십시오.\n{command}\n듣고 계십니까, Kernel?", "Do not make me repeat myself.\n{command}\nAre you listening, Kernel?") },
                onComplete = new StoryEffects { triggers = new[] { new TriggerValue { id = "terminal.onboarding.complete", value = true } } } };
            var texts = ScriptableObject.CreateInstance<ContentTextTableAsset>(); texts.name = "KernelOnboardingTexts"; texts.tableId = "kernel-onboarding-texts"; texts.entries = rows.ToArray();
            foreach (TerminalDialogueLine line in asset.conversation.angryReplies) line.emotion = TerminalEmotion.Angry;
            asset.textTables = new[] { texts }; return asset;
        }
    }
}
