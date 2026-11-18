using UnityEngine;

namespace Matoi.AI
{
    /// <summary>ボスの攻撃範囲を地面に表示する。内側の印が外枠まで広がった瞬間に攻撃が来る。</summary>
    public class GroundTelegraph : MonoBehaviour
    {
        static readonly Color Outer = new Color(1f, 0.25f, 0.1f, 0.22f), Inner = new Color(1f, 0.35f, 0.1f, 0.55f);

        Transform fill;
        Vector3 fullScale;
        float start, seconds;
        bool grows2D;

        /// <summary>円の範囲を表示する。</summary>
        public static void Circle(Material material, Vector3 center, float radius, float seconds)
        {
            var root = new GameObject("Telegraph").AddComponent<GroundTelegraph>();
            root.transform.position = center + Vector3.up * 0.05f;
            var size = new Vector3(radius * 2f, 0.01f, radius * 2f);
            Piece(PrimitiveType.Cylinder, root.transform, material, Outer, size, Vector3.zero);
            root.Begin(Piece(PrimitiveType.Cylinder, root.transform, material, Inner, size, Vector3.up * 0.01f), size, seconds, true);
        }

        /// <summary>直線の範囲を表示する。</summary>
        public static void Line(Material material, Vector3 start, Vector3 direction, float length, float width, float seconds)
        {
            var root = new GameObject("Telegraph").AddComponent<GroundTelegraph>();
            root.transform.SetPositionAndRotation(start + Vector3.up * 0.05f, Quaternion.LookRotation(direction));
            var size = new Vector3(width, 0.01f, length);
            Vector3 middle = Vector3.forward * (length * 0.5f);
            Piece(PrimitiveType.Cube, root.transform, material, Outer, size, middle);
            root.Begin(Piece(PrimitiveType.Cube, root.transform, material, Inner, size, middle + Vector3.up * 0.01f), size, seconds, false);
        }

        static Transform Piece(PrimitiveType shape, Transform parent, Material material, Color color, Vector3 scale, Vector3 position)
        {
            var go = GameObject.CreatePrimitive(shape);
            var box = go.GetComponent<Collider>();
            box.enabled = false;
            Destroy(box);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var meshRenderer = go.GetComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            if (material) meshRenderer.material = material;
            meshRenderer.material.color = color;
            return go.transform;
        }

        void Begin(Transform inner, Vector3 scale, float duration, bool circle)
        {
            fill = inner;
            fullScale = scale;
            seconds = Mathf.Max(0.05f, duration);
            start = Time.time;
            grows2D = circle;
            Apply(0f);
        }

        void Apply(float t)
        {
            fill.localScale = grows2D ? new Vector3(fullScale.x * t, fullScale.y, fullScale.z * t) : new Vector3(fullScale.x * t, fullScale.y, fullScale.z);
        }

        void Update()
        {
            float t = (Time.time - start) / seconds;
            if (t >= 1f) { Destroy(gameObject); return; }
            Apply(t);
        }
    }
}
