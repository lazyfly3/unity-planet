using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpacecraftEditor
{
    public sealed class CommandHistory : MonoBehaviour
    {
        [SerializeField] private ShipAssembly assembly;
        [SerializeField] private ShipHullController hullController;
        [SerializeField] private int capacity = 50;
        private readonly List<Snapshot> snapshots = new List<Snapshot>();
        private int cursor = -1;
        private bool restoring;

        public event Action HistoryChanged;
        public event Action Restored;
        public bool CanUndo => cursor > 0;
        public bool CanRedo => cursor >= 0 && cursor < snapshots.Count - 1;

        public void Configure(ShipAssembly target, ShipHullController hull = null)
        {
            assembly = target;
            if (hull != null)
                hullController = hull;
            snapshots.Clear();
            cursor = -1;
            Record();
        }

        public void Record()
        {
            if (restoring || assembly == null)
                return;
            var state = Capture();
            if (cursor < -1 || cursor >= snapshots.Count)
                cursor = snapshots.Count - 1;
            if (cursor >= 0 && cursor < snapshots.Count && StatesEqual(snapshots[cursor], state))
                return;
            if (cursor < snapshots.Count - 1)
                snapshots.RemoveRange(cursor + 1, snapshots.Count - cursor - 1);
            snapshots.Add(state);
            if (snapshots.Count > capacity)
                snapshots.RemoveAt(0);
            cursor = snapshots.Count - 1;
            HistoryChanged?.Invoke();
        }

        public void Undo()
        {
            if (!CanUndo || assembly == null)
                return;
            cursor--;
            RestoreCurrent();
        }

        public void Redo()
        {
            if (!CanRedo || assembly == null)
                return;
            cursor++;
            RestoreCurrent();
        }

        private void RestoreCurrent()
        {
            restoring = true;
            Snapshot snapshot = snapshots[cursor];
            assembly.RestoreStates(snapshot.parts);
            if (hullController != null && !string.IsNullOrEmpty(snapshot.hullMaterialId))
                hullController.ApplyPaint(snapshot.hullMaterialId);
            restoring = false;
            Restored?.Invoke();
            HistoryChanged?.Invoke();
        }

        private Snapshot Capture()
        {
            return new Snapshot
            {
                parts = Clone(assembly.CaptureStates()),
                hullMaterialId = hullController == null ? string.Empty : hullController.CurrentMaterialId
            };
        }

        private static List<PlacedPartState> Clone(IReadOnlyList<PlacedPartState> source)
        {
            var result = new List<PlacedPartState>(source.Count);
            foreach (var item in source)
                result.Add(item.Clone());
            return result;
        }

        private static bool StatesEqual(Snapshot left, Snapshot right)
        {
            if (left.hullMaterialId != right.hullMaterialId)
                return false;
            IReadOnlyList<PlacedPartState> leftParts = left.parts;
            IReadOnlyList<PlacedPartState> rightParts = right.parts;
            if (leftParts == null || rightParts == null)
                return leftParts == rightParts;
            if (leftParts.Count != rightParts.Count)
                return false;
            for (var i = 0; i < leftParts.Count; i++)
            {
                var a = leftParts[i];
                var b = rightParts[i];
                if (a.runtimeId != b.runtimeId || a.partId != b.partId ||
                    a.mirrorGroupId != b.mirrorGroupId || a.materialId != b.materialId ||
                    a.activationKey != b.activationKey || a.weaponGroup != b.weaponGroup)
                    return false;
                if ((a.localPosition - b.localPosition).sqrMagnitude > 0.000001f)
                    return false;
                if (Quaternion.Angle(a.localRotation, b.localRotation) > 0.01f)
                    return false;
                if (Mathf.Abs(a.uniformScale - b.uniformScale) > 0.0001f)
                    return false;
            }
            return true;
        }

        private sealed class Snapshot
        {
            public List<PlacedPartState> parts;
            public string hullMaterialId;
        }
    }
}
