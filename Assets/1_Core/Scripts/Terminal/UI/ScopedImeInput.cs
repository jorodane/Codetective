using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
#endif

namespace Codetective.Terminal
{
    /// <summary>Give uGUI and our completion code the same, field-owned composition string.</summary>
    public sealed class ScopedImeInput : BaseInput
    {
        private BaseInput source;
        private BaseInput previousOverride;
        private BaseInputModule module;
        private CompletingInputField owner;
        private ImeSession session;
        private string pendingEcho = "";
#if ENABLE_INPUT_SYSTEM
        private Keyboard keyboard;
#endif

        public static ScopedImeInput Acquire(CompletingInputField field, ImeSession session)
        {
            BaseInputModule module = EventSystem.current != null ? EventSystem.current.currentInputModule : null;
            if (module == null) return null;
            var scope = module.inputOverride as ScopedImeInput;
            if (scope == null)
            {
                BaseInput input = module.input;
                BaseInput oldOverride = module.inputOverride;
                scope = module.gameObject.AddComponent<ScopedImeInput>();
                scope.source = input; scope.previousOverride = oldOverride; scope.module = module;
                module.inputOverride = scope;
            }
            scope.owner = field;
            scope.session = session;
            session.Begin(scope.RawComposition);
            session.ExpectCommitEcho(scope.pendingEcho); scope.pendingEcho = "";
            scope.AttachKeyboard();
            return scope;
        }

        public string RawComposition => source != null ? source.compositionString ?? "" : "";
        public bool Owns(CompletingInputField field) => owner == field;
        public override string compositionString
        {
            get
            {
                if (owner != null && session != null) return session.Read(RawComposition);
                // InputField's caret getters consult the module even while unfocused.
                GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (selected != null && selected.GetComponent<CompletingInputField>() != null) return "";
                return RawComposition;
            }
        }

        public void Release(CompletingInputField field)
        {
            if (owner != field || session == null) return;
            string echo = session.TakeCommitEcho();
            if (echo.Length > 0) pendingEcho = echo;
            owner = null; session = null;
            SetImeEnabled(false);
        }

        public void RestartIme() { SetImeEnabled(false); SetImeEnabled(true); }
        private void SetImeEnabled(bool enabled)
        {
#if ENABLE_INPUT_SYSTEM
            if (!isActiveAndEnabled) return;
            AttachKeyboard();
            if (keyboard != null) keyboard.SetIMEEnabled(enabled);
#endif
        }
        private void AttachKeyboard()
        {
#if ENABLE_INPUT_SYSTEM
            if (keyboard == Keyboard.current) return;
            if (keyboard != null) keyboard.onIMECompositionChange -= CompositionChanged;
            keyboard = Keyboard.current;
            if (keyboard != null) keyboard.onIMECompositionChange += CompositionChanged;
#endif
        }
#if ENABLE_INPUT_SYSTEM
        private void CompositionChanged(IMECompositionString value) { if (owner != null) session?.CompositionChanged(value.ToString()); }
#endif

        public override IMECompositionMode imeCompositionMode
        {
            get => source != null ? source.imeCompositionMode : IMECompositionMode.Auto;
            set { if (source != null) source.imeCompositionMode = value; SetImeEnabled(value == IMECompositionMode.On); }
        }
        public override Vector2 compositionCursorPos
        {
            get => source != null ? source.compositionCursorPos : Vector2.zero;
            set
            {
                if (source != null) source.compositionCursorPos = value;
#if ENABLE_INPUT_SYSTEM
                AttachKeyboard();
                if (keyboard != null) keyboard.SetIMECursorPosition(value);
#endif
            }
        }
        public override bool mousePresent => source != null && source.mousePresent;
        public override bool GetMouseButtonDown(int button) => source != null && source.GetMouseButtonDown(button);
        public override bool GetMouseButtonUp(int button) => source != null && source.GetMouseButtonUp(button);
        public override bool GetMouseButton(int button) => source != null && source.GetMouseButton(button);
        public override Vector2 mousePosition => source != null ? source.mousePosition : Vector2.zero;
        public override Vector2 mouseScrollDelta => source != null ? source.mouseScrollDelta : Vector2.zero;
        public override bool touchSupported => source != null && source.touchSupported;
        public override int touchCount => source != null ? source.touchCount : 0;
        public override Touch GetTouch(int index) => source.GetTouch(index);
        public override float GetAxisRaw(string axisName) => source != null ? source.GetAxisRaw(axisName) : 0;
        public override bool GetButtonDown(string buttonName) => source != null && source.GetButtonDown(buttonName);

        protected override void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            if (keyboard != null) keyboard.onIMECompositionChange -= CompositionChanged;
            keyboard = null;
#endif
            base.OnDisable();
        }
        protected override void OnDestroy()
        {
#if ENABLE_INPUT_SYSTEM
            if (keyboard != null) keyboard.onIMECompositionChange -= CompositionChanged;
            keyboard = null;
#endif
            if (module != null && module.inputOverride == this) module.inputOverride = previousOverride;
            base.OnDestroy();
        }
    }
}
