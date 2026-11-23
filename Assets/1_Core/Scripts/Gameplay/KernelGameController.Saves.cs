using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Codetective.Interaction;
using Codetective.Operations;
using Codetective.Terminal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Codetective.Gameplay
{
    public sealed partial class KernelGameController
    {
        private SaveHistory saveHistory;
        private bool legacyChecked, quickSaving;
        private string legacyError;
        private SaveHistory History => saveHistory ?? (saveHistory = new SaveHistory(Path.Combine(Application.persistentDataPath, "Codetective", "saves"),
            record => JsonUtility.ToJson(record, true), json => JsonUtility.FromJson<GameSaveRecord>(json)));
        private void EnsureSaveHistory()
        {
            if (legacyChecked) return;
            legacyChecked = true;
            try
            {
                History.ImportLegacy(LegacySavePath, json =>
                {
                    KernelRuntime saved = RestoreSavedRuntime(json);
                    return new LegacySaveDescription { Day = saved.Day, Minute = saved.Minute, Situation = SaveSituation(saved) };
                });
            }
            catch (Exception error) { legacyError = "이전 자동 저장을 읽을 수 없습니다: " + error.Message; }
        }
        private static string SaveSituation(KernelRuntime runtime)
        {
            AppointmentProgress visit = runtime.CurrentAppointment;
            string state;
            if (runtime.TerminalInputLocked) state = "작전망 안내 수신 중";
            else if (runtime.TerminalAwaitingAnswer) state = "작전망 응답 대기";
            else if (runtime.TerminalConversationActive) state = "작전망 명령 연습";
            else if (visit != null) state = visit.patientName + (visit.phase == VisitPhase.Arriving ? " 입실 중" : visit.phase == VisitPhase.Talking ? " 상담 중" : " 상담 마무리");
            else state = runtime.AwaitingAssignment ? "임무 안내 대기" : runtime.IsDayEnded ? "업무 종료" : "상담 대기";
            int tasks = runtime.Threads.Count(t => t.State != AgentState.Ready);
            return runtime.DaySubtitle + " · " + state + (tasks > 0 ? " · Thread 작전 " + tasks + "건" : "");
        }
        private KernelRuntime RestoreSavedRuntime(string json)
        {
            KernelSnapshot saved = JsonUtility.FromJson<KernelSnapshot>(json);
            if (saved == null) throw new CommandException("저장된 기록이 비어 있습니다.");
            KernelRuntime candidate = CreateRuntime(); candidate.Restore(saved); ClinicScenario.EnsureKernelProfile(candidate); return candidate;
        }
        private bool TrySave(GameSaveKind kind)
        {
            if (!initialized || Runtime == null || !IsSessionActive || changingScene) return false;
            try
            {
                EnsureSaveHistory();
                History.Save(JsonUtility.ToJson(Runtime.Capture()), kind, Runtime.Day, Runtime.Minute, SaveSituation(Runtime));
                if (inspection != null) inspection.GetComponent<PauseMenuView>().RefreshSaveList();
                narrativeDirty = false; Feedback = "저장되었습니다."; return true;
            }
            catch (Exception error)
            {
                Feedback = "저장 실패: " + error.Message; narrativeSaveAt = Time.unscaledTime + 2f;
                Debug.LogWarning(Feedback, this); return false;
            }
        }
        public void AutoSave()
        {
            if (quickSaving) { QueueNarrativeSave(); return; }
            if (!TrySave(GameSaveKind.Auto) && IsSessionActive) ui?.ShowSaveStatus("자동 저장 실패", 3f);
        }
        public void SaveNow()
        {
            if (!initialized || Runtime == null || !IsSessionActive || changingScene) return;
            if (quickSaving) { inspection.GetComponent<PauseMenuView>().ShowFeedback("빠른 저장 중입니다."); return; }
            TextInputFocus.ReleaseAll();
            bool saved = TrySave(GameSaveKind.Manual);
            inspection.GetComponent<PauseMenuView>().ShowFeedback(Feedback);
            ui?.ShowSaveStatus(saved ? "저장되었습니다." : "저장 실패", 2f);
        }
        public void QuickSave()
        {
            if (!presentationInitialized || !IsSessionActive || changingScene || quickSaving || IsDateTransitioning || IsVisitPresenting ||
                inspection.NavigationBlocked || inspection.TextInputFocused || ui.IsCompositionProtected || (Monitor?.IsCompositionProtected ?? false)) return;
            StartCoroutine(SaveQuickly());
        }
        private IEnumerator SaveQuickly()
        {
            quickSaving = true; float started = Time.unscaledTime;
            ui.ShowSaveStatus("빠른 저장 중...", 30f);
            yield return null; // Render the notice before the synchronous file write.
            bool saved = TrySave(GameSaveKind.Quick);
            while (Time.unscaledTime < started + 0.35f) yield return null;
            quickSaving = false;
            ui.ShowSaveStatus(saved ? "저장되었습니다." : "빠른 저장 실패", 2f);
        }
        private bool TryReadSave(string id, out KernelRuntime restored, out string error)
        {
            restored = null;
            try { EnsureSaveHistory(); restored = RestoreSavedRecord(History.Read(id)); error = null; return true; }
            catch (Exception failure) { error = "저장 기록을 읽을 수 없습니다: " + failure.Message; return false; }
        }
        private KernelRuntime RestoreSavedRecord(GameSaveRecord record)
        {
            KernelRuntime candidate = RestoreSavedRuntime(record.snapshot);
            if (candidate.Day != record.day || Math.Abs(candidate.Minute - record.minute) > 0.001) throw new CommandException("게임 시각과 저장 정보가 일치하지 않습니다.");
            return candidate;
        }
        private IReadOnlyList<SystemSaveInfo> ReadSaveInfos()
        {
            EnsureSaveHistory();
            var result = new List<SystemSaveInfo>();
            try
            {
                foreach (GameSaveSlot slot in History.Slots)
                {
                    GameSaveRecord record = slot.Record;
                    if (!slot.Available) { result.Add(new SystemSaveInfo { Id = slot.Id, Situation = slot.Error }); continue; }
                    string kind = record.kind == GameSaveKind.Auto ? "Auto" : record.kind == GameSaveKind.Quick ? "빠른 저장" : "수동 저장";
                    string situation = (record.situation ?? "").Replace('\n', ' ').Replace('\r', ' ');
                    if (situation.Length > 40) situation = situation.Substring(0, 40) + "…";
                    string stamp = DateTimeOffset.ParseExact(record.savedAtUtc, "O", CultureInfo.InvariantCulture).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                    result.Add(new SystemSaveInfo { Id = slot.Id, Available = true, Day = record.day,
                        GameTime = KernelRuntime.TimeOfDay(record.minute), Kind = kind, Situation = situation, SavedAt = stamp });
                }
                if (result.Count == 0 && legacyError != null) result.Add(new SystemSaveInfo { Situation = legacyError });
            }
            catch (Exception error) { result.Add(new SystemSaveInfo { Situation = "저장 목록을 읽을 수 없습니다: " + error.Message }); }
            return result;
        }
        public void LoadGame(string id)
        {
            if (changingScene || quickSaving) return;
            RestartScene(SessionStart.Continue, id);
        }
        public void ReturnToTitle()
        {
            if (changingScene) return;
            if (quickSaving) { inspection.GetComponent<PauseMenuView>().ShowFeedback("빠른 저장 중입니다."); return; }
            if (IsSessionActive && !TrySave(GameSaveKind.Auto)) { inspection.GetComponent<PauseMenuView>().ShowFeedback(Feedback); return; }
            changingScene = true; inspection.NavigationBlocked = true;
            StartCoroutine(ShowTitleCovered());
        }
        private IEnumerator ShowTitleCovered()
        {
            yield return Transitions.CoverScene();
            IsSessionActive = false; ui.HideContext(); inspection.ShowTitleMenu();
            yield return Transitions.RevealScene();
            changingScene = false;
        }
        private void RestartScene(SessionStart start, string saveId = null)
        {
            if (changingScene) return;
            changingScene = true; inspection.NavigationBlocked = true;
            StartCoroutine(RestartCovered(start, saveId));
        }
        private IEnumerator RestartCovered(SessionStart start, string saveId)
        {
            yield return Transitions.CoverScene();
            if (start == SessionStart.Continue && !TryReadSave(saveId, out nextRuntime, out string loadError))
            {
                yield return Transitions.RevealScene(); changingScene = false; inspection.NavigationBlocked = inspection.PresentingOnTerminal;
                inspection.GetComponent<PauseMenuView>().ShowFeedback(loadError); yield break;
            }
            nextStart = start; nextSaveId = saveId; Transitions.PrepareSceneReveal();
            inspection.PrepareForSceneChange();
            try
            {
                Scene scene = SceneManager.GetActiveScene();
                if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex); else SceneManager.LoadScene(scene.name);
            }
            catch (Exception error)
            {
                changingScene = false; nextStart = SessionStart.Title; nextSaveId = null; nextRuntime = null; Transitions.CancelSceneReveal();
                inspection.NavigationBlocked = false; StartCoroutine(Transitions.RevealScene());
                if (IsSessionActive) inspection.ShowPauseMenu(); else inspection.ShowTitleMenu();
                inspection.GetComponent<PauseMenuView>().ShowFeedback("화면을 열 수 없습니다: " + error.Message);
            }
        }
        private void QuitGame()
        {
            if (IsSessionActive && !TrySave(GameSaveKind.Auto)) { inspection.GetComponent<PauseMenuView>().ShowFeedback(Feedback); return; }
            changingScene = true; inspection.PrepareForSceneChange();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
        private void QueueNarrativeSave() { if (!IsSessionActive || changingScene) return; narrativeDirty = true; narrativeSaveAt = Time.unscaledTime + 0.3f; }
        private void OnApplicationPause(bool paused) { if (paused && (narrativeDirty || quickSaving)) TrySave(GameSaveKind.Auto); }
        private void OnApplicationQuit() { if (narrativeDirty || quickSaving) TrySave(GameSaveKind.Auto); }
    }
}
