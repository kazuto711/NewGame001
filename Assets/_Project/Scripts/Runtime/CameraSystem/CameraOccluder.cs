using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Matoi.CameraSystem
{
    /// <summary>
    /// カメラとプレイヤーの間に入ったら見えなくしてよい物（家・木など）に付ける目印。
    /// 見えなくしても影は残す（ShadowsOnly）ので、そこに物があることは分かる。
    /// </summary>
    public class CameraOccluder : MonoBehaviour
    {
        static readonly List<CameraOccluder> all = new List<CameraOccluder>();
        public static IReadOnlyList<CameraOccluder> All => all;

        Renderer[] renderers;
        ShadowCastingMode[] original;
        public Bounds Bounds { get; private set; }
        public bool IsHidden { get; private set; }
        public float LastOccludingTime { get; set; } = -999f;

        void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>();
            original = new ShadowCastingMode[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) original[i] = renderers[i].shadowCastingMode;
            RecalculateBounds();
        }

        void OnEnable() => all.Add(this);
        void OnDisable() { all.Remove(this); SetHidden(false); }

        public void RecalculateBounds()
        {
            if (renderers == null || renderers.Length == 0) { Bounds = new Bounds(transform.position, Vector3.zero); return; }
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            Bounds = b;
        }

        public void SetHidden(bool hidden)
        {
            if (hidden == IsHidden || renderers == null) return;
            IsHidden = hidden;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i]) continue;
                renderers[i].shadowCastingMode = hidden ? ShadowCastingMode.ShadowsOnly : original[i];
            }
        }
    }
}
