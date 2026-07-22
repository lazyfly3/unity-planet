using UnityEngine;
using UnityEngine.EventSystems;

namespace SpacecraftEditor
{
    public sealed class ScaleSliderCommitter : MonoBehaviour, IPointerUpHandler
    {
        private BuildModeController controller;

        public void Configure(BuildModeController target)
        {
            controller = target;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            controller?.CommitScale();
        }
    }
}
