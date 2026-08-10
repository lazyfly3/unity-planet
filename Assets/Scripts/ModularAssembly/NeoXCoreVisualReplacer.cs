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
        private const string PresentationBlocker =
            "NeoXCoreVisualReplacer";

        private ModularContentService contentService;
        private GridAssemblyPresenter presenter;
        private ModularContentRecord coreRecord;
        private bool upgradeRunning;

        public bool IsReady { get; private set; }

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
                presenter.SetPresentationBlocked(
                    PresentationBlocker,
                    false);
                IsReady = true;
                yield break;
            }

            presenter.Rebuilt += RequestUpgrade;
            RequestUpgrade();
        }

        private void RequestUpgrade()
        {
            if (!upgradeRunning)
            {
                IsReady = false;
                presenter?.SetPresentationBlocked(
                    PresentationBlocker,
                    true);
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
                IsReady = true;
                presenter?.SetPresentationBlocked(
                    PresentationBlocker,
                    false);
                yield break;
            }

            GameObject loaded = null;
            yield return contentService.InstantiateAsync(
                coreRecord,
                coreView.transform,
                value => loaded = value);
            presenter?.RefreshPresentationVisibility();
            if (coreView == null || loaded == null)
            {
                upgradeRunning = false;
                IsReady = true;
                presenter?.SetPresentationBlocked(
                    PresentationBlocker,
                    false);
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
            IsReady = true;
            presenter.SetPresentationBlocked(
                PresentationBlocker,
                false);
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
                presenter.SetPresentationBlocked(
                    PresentationBlocker,
                    false);
            }
        }
    }
}
