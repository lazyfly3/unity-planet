using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class GalaxyMapVisitedCellInput : MonoBehaviour, IPointerClickHandler
{
    GalaxyMapController controller;
    int visitedPlanetIndex;

    public void Configure(GalaxyMapController owner, int index)
    {
        controller = owner;
        visitedPlanetIndex = index;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (controller == null)
            return;

        if (eventData.button == PointerEventData.InputButton.Left)
            controller.SelectVisitedPlanet(visitedPlanetIndex);
        else if (eventData.button == PointerEventData.InputButton.Right)
            controller.OpenVisitedPlanetContextMenu(visitedPlanetIndex, eventData.position);
    }
}
