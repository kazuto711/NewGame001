using Matoi.CameraSystem;
using Matoi.Combat;
using UnityEngine;

namespace Matoi.UI
{
    /// <summary>
    /// 三人称で構えた時の照準マーク（画面中央の十字）。
    ///  - 線のすき間＝今の弾のばらつき（動く・撃ち続けると広がる）
    ///  - 敵に当たると「×」のヒットマークが一瞬出る（倒すと赤）
    /// 見下ろし視点では出さない（レーザーの線で狙う）。
    /// </summary>
    public class Crosshair : MonoBehaviour
    {
        [SerializeField] WeaponHolder holder;
        [SerializeField] WeaponShooter shooter;
        [SerializeField] Color color = new Color(1f, 1f, 1f, 0.9f);
        [SerializeField] float lineLength = 9f;
        [SerializeField] float thickness = 2f;
        [SerializeField] float minGap = 4f;
        [SerializeField] float hitMarkerTime = 0.15f;

        float hitUntil;
        bool lastHitKilled;
        float shownGap;

        public bool IsVisible { get; private set; }
        public float GapPixels => shownGap;
        public int HitMarkers { get; private set; }

        void Awake()
        {
            if (!holder) holder = GetComponent<WeaponHolder>();
            if (!shooter) shooter = GetComponent<WeaponShooter>();
        }

        void OnEnable() { if (shooter) shooter.Hit += OnHit; }
        void OnDisable() { if (shooter) shooter.Hit -= OnHit; }

        void OnHit(RaycastHit hit, IDamageable target)
        {
            if (target == null) return;
            hitUntil = Time.unscaledTime + hitMarkerTime;
            lastHitKilled = target.IsDead;
            HitMarkers++;
        }

        void Update()
        {
            IsVisible = CameraModeSwitcher.Mode == CameraMode.ThirdPerson && holder && holder.IsAiming;
            // ばらつき（度）→ 画面上のすき間（ピクセル）
            var cam = Camera.main;
            float spread = shooter ? shooter.CurrentSpread : 0f;
            float gap = minGap;
            if (cam)
                gap += Mathf.Tan(spread * Mathf.Deg2Rad) / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * Screen.height * 0.5f;
            shownGap = Mathf.Lerp(shownGap, gap, 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));
        }

        void OnGUI()
        {
            if (!IsVisible) return;
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            float g = shownGap, l = lineLength, t = thickness;
            DrawBox(cx - t / 2, cy - g - l, t, l, color);   // 上
            DrawBox(cx - t / 2, cy + g, t, l, color);       // 下
            DrawBox(cx - g - l, cy - t / 2, l, t, color);   // 左
            DrawBox(cx + g, cy - t / 2, l, t, color);       // 右
            DrawBox(cx - 1, cy - 1, 2, 2, color);           // 中心の点

            if (Time.unscaledTime < hitUntil)
            {
                // ヒットマーク（×）
                var c = lastHitKilled ? new Color(1f, 0.25f, 0.2f) : Color.white;
                var m = GUI.matrix;
                for (int i = 0; i < 4; i++)
                {
                    GUIUtility.RotateAroundPivot(45f + 90f * i, new Vector2(cx, cy));
                    DrawBox(cx - 1.5f, cy - 16f, 3f, 8f, c);
                    GUI.matrix = m;
                }
            }
        }

        static void DrawBox(float x, float y, float w, float h, Color c)
        {
            GUI.color = new Color(0, 0, 0, c.a * 0.6f);              // 見やすいよう薄い影
            GUI.DrawTexture(new Rect(x + 1, y + 1, w, h), Texture2D.whiteTexture);
            GUI.color = c;
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
