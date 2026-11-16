namespace Codetective.Interaction
{
    public enum InspectionMode { Overview, Inspecting, Menu }
    public enum EscapeAction { OpenMenu, CloseMenu }

    /// <summary>One Escape press produces exactly one navigation action.</summary>
    public sealed class InspectionFlow
    {
        public InspectionMode Mode { get; private set; } = InspectionMode.Overview;
        private InspectionMode beforeMenu = InspectionMode.Overview;

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
                default:
                    beforeMenu = Mode;
                    Mode = InspectionMode.Menu;
                    return EscapeAction.OpenMenu;
            }
        }
    }
}
