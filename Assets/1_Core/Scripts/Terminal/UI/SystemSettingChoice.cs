using System;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    public sealed class SystemSettingChoice : Button
    {
        public Action<int> Adjust;
        public override void OnMove(AxisEventData eventData)
        {
            if (IsActive() && IsInteractable() && (eventData.moveDir == MoveDirection.Left || eventData.moveDir == MoveDirection.Right))
            { Adjust?.Invoke(eventData.moveDir == MoveDirection.Left ? -1 : 1); eventData.Use(); return; }
            base.OnMove(eventData);
        }
    }
}
