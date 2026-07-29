using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    public sealed class NeoXCoreVisualReplacer : MonoBehaviour
    {
        private const string CoreSourceId = "block:core:core_heavy_222";
        private const string CoreVisualName = "NeoXCoreHeavy222";

        private ModularContentService contentService;
        private GridAssemblyPresenter presenter;
        private ModularContentRecord coreRecord;
        private bool upgradeRunning;

        private IEnumerator Start()
        {
            while (contentService == null || !contentService.IsReady || presenter == null)
            {
                contentService = FindObjectOfType<ModularContentService>();
                presenter = FindObjectsOfType<GridAssemblyPresenter>()
                    .FirstOrDefault(item => item != null && item.name == "GridShip");
                yield return null;
            }

            coreRecord = contentService.Catalog.Items.FirstOrDefault(record =>
                record != null &&
                string.Equals(record.sourceId, CoreSourceId, StringComparison.OrdinalIgnoreCase));
            if (coreRecord == null)
            {
                Debug.LogWarning("未找到 NeoX 2x2x2 核心资源: " + CoreSourceId);
                yield break;
            }

            presenter.Rebuilt += RequestUpgrade;
            RequestUpgrade();
        }

        private void RequestUpgrade()
        {
            if (!upgradeRunning)
            {
                StartCoroutine(UpgradeCore());
            }
        }

        private IEnumerator UpgradeCore()
        {
            upgradeRunning = true;
            GridModuleView coreView = FindCoreView();
            if (coreView == null || coreView.transform.Find(CoreVisualName) != null)
            {
                upgradeRunning = false;
                yield break;
            }

            GameObject loaded = null;
            yield return contentService.InstantiateAsync(
                coreRecord,
                coreView.transform,
                value => loaded = value);
            if (coreView == null || loaded == null)
            {
                upgradeRunning = false;
                yield break;
            }

            loaded.name = CoreVisualName;
            foreach (Collider collider in loaded.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
            foreach (Renderer renderer in coreView.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.transform.IsChildOf(loaded.transform))
                {
                    renderer.enabled = false;
                }
            }

            upgradeRunning = false;
        }

        private GridModuleView FindCoreView()
        {
            if (presenter == null)
            {
                return null;
            }

            return presenter.Views.Values.FirstOrDefault(view =>
                view?.Record?.Definition != null &&
                string.Equals(
                    view.Record.Definition.ModuleId,
                    "core",
                    StringComparison.OrdinalIgnoreCase));
        }

        private void OnDestroy()
        {
            if (presenter != null)
            {
                presenter.Rebuilt -= RequestUpgrade;
            }
        }
    }
}
