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
            TerminalDialogueLine OfficeLine(string key, string ko, string en)
            {
                TerminalDialogueLine line = Line(key, ko, en);
                line.speaker = "사무국";
                line.speakerKey = "onboarding.office";
                return line;
            }
            Text("onboarding.speaker", "현장지원과", "Field Support");
            Text("onboarding.office", "사무국", "Secretariat");
            Text("profile.regularVisitor.name", "한정우", "Han Jeong-woo");
            Text("terminal.dialogue.answer.options", "대답>", "Answer>");
            Text("terminal.dialogue.answer.none", "지금 답할 질문이 없습니다.", "There is no question to answer right now.");
            Text("terminal.dialogue.answer.invalid", "표시된 선택지 중 하나를 입력하세요.", "Enter one of the displayed options.");
            Text("terminal.dialogue.answer.sent", "응답을 전송했습니다.", "Answer sent.");
            var steps = new[] {
                new TerminalConversationStep { id = "help", lines = new[] {
                    Line("welcome", "Kernel을 찾으셨군요.", "You found Kernel."),
                    Line("connection", "연결을 확인했습니다. 작전망의 기본 사용법부터 안내하겠습니다.", "The line is open. Let's go through the basics of the operations network."),
                    Line("listen", "입력창이 비어 있을 때 Next를 눌러 안내를 넘길 수 있습니다. 다음 명령을 입력하고 Send를 누르십시오.\n{command}", "Use Next to continue the briefing while the input is empty. Enter {command} and select Send.") },
                    goal = new TerminalCommandGoal { kind = TerminalGoalKind.Help } },
                new TerminalConversationStep { id = "reference", previousIds = new[] { "allocate" }, lines = new[] {
                    Line("permissions", "잘하셨습니다. 권한 목록은 현재 사용할 수 있는 정보와 행동을 보여줍니다.", "Good. The permissions list shows the information and actions currently available to you."),
                    Line("reference", "{slot} = 까지 입력한 뒤 색으로 표시된 [[hq|지휘소]]를 눌러 넣으십시오. Send를 누르면 {slot}으로 그 대상을 부를 수 있습니다.",
                        "Type {slot} =, then click [[hq|headquarters]] to insert it. Select Send to register {slot} as its name.") },
                    goal = new TerminalCommandGoal { kind = TerminalGoalKind.BindReference, targetId = "hq" } },
                new TerminalConversationStep { id = "invoke", lines = new[] {
                    Line("substitution", "이제 지휘소를 가리키는 이름은 {slot}입니다.", "Now you can use {slot} to refer to headquarters."),
                    Line("dot", "대상 뒤에 마침표를 입력하면 권한 목록이 나옵니다. 목록에서 작업을 선택한 뒤 Send를 눌러 명령을 보내십시오.", "Type a dot after a target to see its permissions. Select an action from the list, then select Send to run the command."),
                    Line("invoke", "연결 상태를 확인하겠습니다. 다음 명령을 입력하십시오:\n{command}", "Let's check the connection. Enter {command}.") },
                    goal = new TerminalCommandGoal { kind = TerminalGoalKind.Invoke, targetId = "hq", operation = "checkComms" } },
                new TerminalConversationStep { id = "release", lines = new[] {
                    Line("release", "통신 확인. 마지막으로 다음 명령으로 연습용 치환문을 폐기하겠습니다:\n{command}", "Connection confirmed. Finally, discard the exercise substitution by entering {command}.") },
                    goal = new TerminalCommandGoal { kind = TerminalGoalKind.Free } },
                new TerminalConversationStep
                {
                    id = "oath",
                    lines = new[] {
                        OfficeLine("oath", "귀하는 Kernel로서 성실히 임무를 수행하고 심각한 정보 누출에 대한 차단 활동에 적극적으로 임할 것임을 맹세할 수 있습니까? 이 결정은 귀하의 Kernel 직무 수행에 있어 가장 중요한 동의이며, 실제 서류상 동의 행위와 동일한 법적 효력을 가집니다.",
                            "Do you swear to carry out your duties faithfully as Kernel and to take an active role in containing serious information leaks? This is the most important consent you give as Kernel and has the same legal effect as signed written consent.")
                    },
                    answers = new[]
                    {
                        new TerminalAnswer
                        {
                            key = "y",
                            reply = new[] { OfficeLine("oath.accepted", "맹세를 접수했습니다. 동의가 작전 기록에 남았습니다.", "Your oath has been received. Your consent is on record.") },
                            effects = new StoryEffects { triggers = new[] { new TriggerValue { id = "kernel.oath.accepted", value = true } } }
                        },
                        new TerminalAnswer
                        {
                            key = "n",
                            reply = new[] { OfficeLine("oath.declined", "동의하지 않으신 것으로 기록했습니다. 안내를 계속하겠습니다.", "Your refusal is on record. The briefing will continue.") },
                            effects = new StoryEffects { triggers = new[] { new TriggerValue { id = "kernel.oath.declined", value = true } } }
                        }
                    }
                },
                new TerminalConversationStep { id = "finish", lines = new[] {
                    Line("world", "치환문을 폐기해도 실제 지휘소는 사라지지 않습니다. 대상을 부르는 규칙만 정리한 것입니다.", "Discarding a substitution does not remove headquarters. You have only removed a rule for referring to it."),
                    Line("coffee", "[[hq|지휘소]]를 눌러 넣고 마침표를 입력하면 권한 목록에서 커피 요청도 찾을 수 있습니다.",
                        "Click [[hq|headquarters]] and type a dot to find the coffee request in its permissions."),
                    OfficeLine("finish", "곧 아홉 시 예약 손님이 들어옵니다. 말풍선을 눌러 적당히 맞장구만 치시면 됩니다. 상담을 마치고 잠시 쉬시면 첫 임무를 메일로 보내겠습니다.", "Your nine o'clock client is about to arrive. Click the speech bubble to chime in now and then. Take a short break after the session and we will send your first assignment by mail.") } }
            };
            var asset = ScriptableObject.CreateInstance<TerminalConversationAsset>(); asset.name = "KernelOnboarding";
            asset.conversation = new TerminalConversationDefinition { id = "kernel-onboarding", requiredMailAccepted = "government-intro", firstDay = 1, lastDay = 1,
                connectionMode = TerminalConnectionMode.Incoming, redialSeconds = 3, legacyReadMailToSkip = "government-intro",
                koreanVariableBase = "연습",
                speaker = "현장지원과", speakerKey = "onboarding.speaker", pauseWorld = true, replacesStartupGuide = true, unlockCommandsByStep = true, advanceWithEnter = true,
                angryAfter = 3, steps = steps,
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
