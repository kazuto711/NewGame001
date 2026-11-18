using Matoi.Player;
using UnityEngine;

namespace Matoi.UI
{
    /// <summary>
    /// しゃがんでいる間、画面上部に今の状態を出す：［ 隠密 ］→［ 警戒 ］→［ 発見 ］。
    /// 下の細い線は、敵の警戒度。隠密攻撃が決まると、倍率を一瞬表示する。
    /// </summary>
    public class StealthIndicator : MonoBehaviour
    {
        [SerializeField] PlayerStealth stealth;
        [SerializeField] float sneakAttackShowTime = 1.4f;

        GUIStyle style, popupStyle;
        float popupUntil, popupMultiplier;

        /// <summary>今表示している文字（表示していない時は空）。</summary>
        public string Label => stealth && stealth.IsSneaking ? LabelOf(stealth.Status) : "";

        static string LabelOf(StealthStatus s) =>
            s == StealthStatus.Detected ? "［ 発見 ］" : s == StealthStatus.Caution ? "［ 警戒 ］" : "［ 隠密 ］";

        static Color ColorOf(StealthStatus s) =>
            s == StealthStatus.Detected ? new Color(1f, 0.3f, 0.22f)
            : s == StealthStatus.Caution ? new Color(1f, 0.82f, 0.3f)
            : new Color(0.72f, 0.92f, 0.72f);

        void Awake() { if (!stealth) stealth = GetComponent<PlayerStealth>(); }
        void OnEnable() { if (stealth) stealth.SneakAttacked += OnSneakAttack; }
        void OnDisable() { if (stealth) stealth.SneakAttacked -= OnSneakAttack; }

        void OnSneakAttack(float multiplier)
        {
            popupMultiplier = multiplier;
            popupUntil = Time.unscaledTime + sneakAttackShowTime;
        }

        void OnGUI()
        {
            if (!stealth) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 26, fontStyle = FontStyle.Bold };
                popupStyle = new GUIStyle(style) { fontSize = 32 };
            }

            float cx = Screen.width * 0.5f;
            if (stealth.IsSneaking)
            {
                var color = ColorOf(stealth.Status);
                DrawText(new Rect(cx - 160f, 26f, 320f, 40f), Label, style, color);
                const float barWidth = 150f;
                float x = cx - barWidth * 0.5f;
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(x, 68f, barWidth, 4f), Texture2D.whiteTexture);
                GUI.color = color;
                GUI.DrawTexture(new Rect(x, 68f, barWidth * Mathf.Clamp01(stealth.Awareness), 4f), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            if (Time.unscaledTime < popupUntil)
                DrawText(new Rect(cx - 220f, Screen.height * 0.3f, 440f, 50f), $"隠密攻撃  ×{popupMultiplier:0}", popupStyle, new Color(1f, 0.85f, 0.4f));
        }

        static void DrawText(Rect rect, string text, GUIStyle style, Color color)
        {
            style.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style);
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
        }
    }
}
