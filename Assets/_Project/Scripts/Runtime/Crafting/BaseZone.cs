using UnityEngine;

namespace Matoi.Crafting
{
    /// <summary>拠点の範囲。この中にいる間は、敵に見つからず、追われることもない。</summary>
    public class BaseZone : MonoBehaviour
    {
        public static BaseZone Instance { get; private set; }

        [SerializeField] float radius = 9f;

        public float Radius => radius;

        public void Configure(float zoneRadius) => radius = zoneRadius;

        void OnEnable() => Instance = this;
        void OnDisable() { if (Instance == this) Instance = null; }

        /// <summary>その位置が拠点の中か。</summary>
        public static bool Contains(Vector3 position)
        {
            if (!Instance) return false;
            Vector3 d = position - Instance.transform.position;
            d.y = 0f;
            return d.sqrMagnitude <= Instance.radius * Instance.radius;
        }
    }
}
