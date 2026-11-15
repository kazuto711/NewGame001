using UnityEngine;

namespace Matoi.Combat
{
    /// <summary>
    /// 頭上の体力バー。ダメージを受けた時だけしばらく表示する（画面をすっきりさせるため）。
    /// 2枚の板（背景・中身）をカメラに向けて表示するだけの軽い仕組み。
    /// </summary>
    public class FloatingHealthBar : MonoBehaviour
    {
        [SerializeField] Health health;
        [SerializeField] Material backgroundMaterial;
        [SerializeField] Material fillMaterial;
        [SerializeField] float height = 2.25f;
        [SerializeField] Vector2 size = new Vector2(0.9f, 0.09f);
        [SerializeField] float showTime = 4f;

        Transform root, fill;
        float visibleUntil, shownFraction = 1f;

        public bool IsVisible => root && root.gameObject.activeSelf;
        public float Fraction => health ? health.Current / Mathf.Max(1f, health.Max) : 0f;

        void Awake()
        {
            if (!health) health = GetComponent<Health>();
            root = new GameObject("HealthBar").transform;
            root.SetParent(transform, false);
            Quad("Background", root, backgroundMaterial, new Vector3(size.x + 0.04f, size.y + 0.04f, 1f), 0.001f);
            fill = Quad("Fill", root, fillMaterial, new Vector3(size.x, size.y, 1f), 0f).transform;
            root.gameObject.SetActive(false);
            health.OnDamaged += _ => visibleUntil = Time.time + showTime;
        }

        static GameObject Quad(string name, Transform parent, Material mat, Vector3 scale, float back)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = name;
            q.transform.SetParent(parent, false);
            q.transform.localScale = scale;
            q.transform.localPosition = new Vector3(0, 0, back);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return q;
        }

        void LateUpdate()
        {
            bool show = Time.time < visibleUntil && !health.IsDead;
            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
            if (!show) return;
            var cam = Camera.main;
            root.position = transform.position + Vector3.up * height;
            if (cam) root.rotation = cam.transform.rotation;
            shownFraction = Mathf.MoveTowards(shownFraction, Fraction, Time.deltaTime * 2f);
            float f = Mathf.Clamp01(Fraction);
            fill.localScale = new Vector3(size.x * f, size.y, 1f);
            fill.localPosition = new Vector3(-size.x * (1f - f) * 0.5f, 0f, 0f);
        }
    }
}
