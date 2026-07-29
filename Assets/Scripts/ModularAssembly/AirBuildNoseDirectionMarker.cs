using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class AirBuildNoseDirectionMarker : MonoBehaviour
{
    const string LabSceneName = "ModularAssemblyLab";
    const string ShipPresenterName = "GridShip";

    ModularAssemblyLabController controller;
    GridAssemblyPresenter presenter;
    LineRenderer directionLine;
    Material lineMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (!string.Equals(
                SceneManager.GetActiveScene().name,
                LabSceneName,
                StringComparison.OrdinalIgnoreCase)
            || FindObjectOfType<AirBuildNoseDirectionMarker>() != null)
        {
            return;
        }

        new GameObject("AirBuildNoseDirectionMarker")
            .AddComponent<AirBuildNoseDirectionMarker>();
    }

    IEnumerator Start()
    {
        while (controller == null || presenter == null)
        {
            controller = FindObjectOfType<ModularAssemblyLabController>();
            GridAssemblyPresenter[] presenters =
                FindObjectsOfType<GridAssemblyPresenter>();
            for (int index = 0; index < presenters.Length; index++)
            {
                if (presenters[index] != null
                    && presenters[index].name == ShipPresenterName)
                {
                    presenter = presenters[index];
                    break;
                }
            }
            yield return null;
        }

        CreateLine();
        presenter.Rebuilt += RefreshLine;
        RefreshLine();
    }

    void Update()
    {
        if (directionLine != null)
        {
            directionLine.enabled =
                controller != null
                && !controller.IsFlying
                && presenter != null;
        }
    }

    void CreateLine()
    {
        GameObject lineObject =
            new GameObject("ShipNoseDirectionLine");
        lineObject.transform.SetParent(presenter.transform, false);

        directionLine = lineObject.AddComponent<LineRenderer>();
        directionLine.useWorldSpace = false;
        directionLine.positionCount = 2;
        directionLine.alignment = LineAlignment.View;
        directionLine.widthMultiplier = 0.09f;
        directionLine.numCapVertices = 4;
        directionLine.shadowCastingMode = ShadowCastingMode.Off;
        directionLine.receiveShadows = false;
        directionLine.startColor = new Color(0.08f, 0.48f, 1f, 1f);
        directionLine.endColor = new Color(0.2f, 0.72f, 1f, 0.9f);

        Shader shader =
            Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color");
        lineMaterial = new Material(shader);
        Color color = new Color(0.08f, 0.48f, 1f, 1f);
        lineMaterial.color = color;
        if (lineMaterial.HasProperty("_BaseColor"))
            lineMaterial.SetColor("_BaseColor", color);
        directionLine.sharedMaterial = lineMaterial;
    }

    void RefreshLine()
    {
        if (directionLine == null || presenter == null)
            return;

        bool initialized = false;
        Bounds localBounds = new Bounds();
        foreach (GridModuleView view in presenter.Views.Values)
        {
            if (view == null)
                continue;

            Renderer[] renderers =
                view.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null)
                    continue;
                EncapsulateRenderer(
                    renderer,
                    ref localBounds,
                    ref initialized);
            }
        }

        if (!initialized)
        {
            directionLine.enabled = false;
            return;
        }

        Vector3 start = new Vector3(
            localBounds.center.x,
            localBounds.center.y,
            localBounds.max.z + 0.15f);
        float length = Mathf.Clamp(
            Mathf.Max(4f, localBounds.size.z * 0.5f),
            4f,
            14f);
        directionLine.SetPosition(0, start);
        directionLine.SetPosition(1, start + Vector3.forward * length);
        directionLine.enabled =
            controller != null && !controller.IsFlying;
    }

    void EncapsulateRenderer(
        Renderer renderer,
        ref Bounds localBounds,
        ref bool initialized)
    {
        Bounds worldBounds = renderer.bounds;
        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;
        for (int x = 0; x <= 1; x++)
        for (int y = 0; y <= 1; y++)
        for (int z = 0; z <= 1; z++)
        {
            Vector3 worldPoint = new Vector3(
                x == 0 ? min.x : max.x,
                y == 0 ? min.y : max.y,
                z == 0 ? min.z : max.z);
            Vector3 localPoint =
                presenter.transform.InverseTransformPoint(worldPoint);
            if (!initialized)
            {
                localBounds = new Bounds(localPoint, Vector3.zero);
                initialized = true;
            }
            else
            {
                localBounds.Encapsulate(localPoint);
            }
        }
    }

    void OnDestroy()
    {
        if (presenter != null)
            presenter.Rebuilt -= RefreshLine;
        if (lineMaterial != null)
            Destroy(lineMaterial);
    }
}
