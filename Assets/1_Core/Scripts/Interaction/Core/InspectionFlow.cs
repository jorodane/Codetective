namespace Codetective.Interaction
{
    public enum InspectionMode { Overview, Inspecting, Menu }
    public enum EscapeAction { ReturnToOverview, OpenMenu, CloseMenu }

    /// <summary>One Escape press produces exactly one navigation action.</summary>
    public sealed class InspectionFlow
    {
        public InspectionMode Mode { get; private set; } = InspectionMode.Overview;

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
            Mode = InspectionMode.Overview;
            return true;
        }

        public EscapeAction Escape()
        {
            switch (Mode)
            {
                case InspectionMode.Inspecting:
                    Mode = InspectionMode.Overview;
                    return EscapeAction.ReturnToOverview;
                case InspectionMode.Menu:
                    Mode = InspectionMode.Overview;
                    return EscapeAction.CloseMenu;
                default:
                    Mode = InspectionMode.Menu;
                    return EscapeAction.OpenMenu;
            }
        }
    }
}
