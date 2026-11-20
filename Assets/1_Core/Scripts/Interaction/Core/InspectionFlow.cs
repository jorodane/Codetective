namespace Codetective.Interaction
{
    public enum InspectionMode { Overview, Inspecting, Menu }
    public enum EscapeAction { OpenMenu, CloseMenu, ReturnToOverview }

    /// <summary>One Escape press produces exactly one navigation action.</summary>
    public sealed class InspectionFlow
    {
        public InspectionMode Mode { get; private set; } = InspectionMode.Overview;
        private InspectionMode beforeMenu = InspectionMode.Overview;

        public bool CanInspectByPointer(string currentAngleId) => Mode == InspectionMode.Overview ||
            (Mode == InspectionMode.Inspecting && string.Equals(currentAngleId, "drawer", System.StringComparison.OrdinalIgnoreCase));

        public bool Inspect()
        {
            if (Mode == InspectionMode.Menu)
                return false;
            Mode = InspectionMode.Inspecting;
            return true;
        }

        public void ReturnToOverview()
        {
            if (Mode != InspectionMode.Menu)
                Mode = InspectionMode.Overview;
        }

        public bool Resume()
        {
            if (Mode != InspectionMode.Menu)
                return false;
            Mode = beforeMenu;
            return true;
        }

        public EscapeAction Escape()
        {
            switch (Mode)
            {
                case InspectionMode.Menu:
                    Mode = beforeMenu;
                    return EscapeAction.CloseMenu;
                case InspectionMode.Inspecting:
                    Mode = InspectionMode.Overview;
                    return EscapeAction.ReturnToOverview;
                default:
                    OpenMenu();
                    return EscapeAction.OpenMenu;
            }
        }
        public void OpenMenu() { beforeMenu = Mode; Mode = InspectionMode.Menu; }
    }
}
