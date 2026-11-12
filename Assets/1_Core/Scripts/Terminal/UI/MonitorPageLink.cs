using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    [DisallowMultipleComponent, RequireComponent(typeof(Button))]
    public sealed class MonitorPageLink : MonoBehaviour
    {
        [SerializeField] private MonitorPageHost host;
        [SerializeField] private string pageId;
        private void OnEnable() => GetComponent<Button>().onClick.AddListener(OpenPage);
        private void OnDisable() => GetComponent<Button>().onClick.RemoveListener(OpenPage);
        public void OpenPage()
        {
            if (host == null) host = GetComponentInParent<MonitorPageHost>();
            if (host != null) host.ShowPage(pageId);
        }
    }
}
