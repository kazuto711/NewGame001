using System.Collections.Generic;
using Matoi.AI;
using Matoi.Combat;
using Matoi.Equipment;
using Matoi.Missions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Matoi.UI
{
    /// <summary>遊んでいる間の表示：体力と弾、ミッションの目的、目的地の印、無線の字幕、ボスの体力。</summary>
    public class GameHud : MonoBehaviour
    {
        static readonly Color MarkerColor = new Color(0.55f, 0.95f, 1f);

        readonly List<MissionMarker> markers = new List<MissionMarker>();
        Health health;
        WeaponHolder weapons;
        WeaponShooter shooter;
        PowerArmorUser armor;
        MissionTracker tracker;
        GUIStyle labelStyle, smallStyle, titleStyle, radioStyle, markerStyle, bossStyle;
        float objectiveFlashUntil;

        void Awake()
        {
            health = GetComponent<Health>();
            weapons = GetComponent<WeaponHolder>();
            shooter = GetComponent<WeaponShooter>();
            armor = GetComponent<PowerArmorUser>();
            tracker = GetComponent<MissionTracker>();
        }

        void OnEnable() { if (tracker) tracker.StepChanged += OnStepChanged; }
        void OnDisable() { if (tracker) tracker.StepChanged -= OnStepChanged; }

        void OnStepChanged(ObjectiveStep step) => objectiveFlashUntil = Time.unscaledTime + 2.5f;

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame) GameFlow.ShowDevInfo = !GameFlow.ShowDevInfo;
        }

        void OnGUI()
        {
            if (MenuState.IsOpen) return;
            EnsureStyles();
            DrawVitals();
            DrawObjective();
            DrawMarkers();
            DrawRadio();
            DrawBoss();
        }

        void EnsureStyles()
        {
            if (labelStyle != null) return;
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold };
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            radioStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            markerStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            bossStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        // ---------- 体力と弾 ----------
        void DrawVitals()
        {
            const float width = 280f;
            float x = Screen.width - width - 28f, y = Screen.height - 96f;
            if (health)
            {
                float f = health.Current / Mathf.Max(1f, health.Max);
                Bar(new Rect(x, y, width, 14f), f, f < 0.3f ? new Color(0.95f, 0.25f, 0.18f) : new Color(0.45f, 0.85f, 0.4f));
                Shadowed(new Rect(x, y - 24f, width, 24f), $"体力 {health.Current:0}", smallStyle, Color.white);
            }
            if (armor && armor.IsArmored && armor.Current)
            {
                Bar(new Rect(x, y + 20f, width, 8f), armor.Current.IntegrityFraction, new Color(1f, 0.72f, 0.25f));
            }
            var weapon = weapons ? weapons.CurrentWeapon : null;
            if (!weapon || !shooter) return;
            string reserve = shooter.ReserveAmmo < 0 ? "∞" : shooter.ReserveAmmo.ToString();
            string state = shooter.IsReloading ? "　リロード中" : "";
            Shadowed(new Rect(x, y + 32f, width, 30f), $"{weapon.displayName}　{shooter.AmmoInMagazine} / {reserve}{state}", labelStyle, Color.white);
        }

        static void Bar(Rect rect, float fraction, Color color)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fraction), rect.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // ---------- 目的 ----------
        void DrawObjective()
        {
            if (!tracker || tracker.Current == null) return;
            const float x = 24f, y = 34f, width = 430f;
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(x - 10f, y - 6f, width + 20f, 66f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            Shadowed(new Rect(x, y - 4f, width, 24f), tracker.Mission.title, titleStyle, MarkerColor);
            Color color = Time.unscaledTime < objectiveFlashUntil ? new Color(1f, 0.88f, 0.55f) : Color.white;
            Shadowed(new Rect(x, y + 20f, width, 34f), tracker.ObjectiveText, labelStyle, color);
        }

        // ---------- 目的地の印 ----------
        void DrawMarkers()
        {
            var cam = Camera.main;
            if (!tracker || !cam) return;
            tracker.CollectMarkers(markers);
            foreach (var marker in markers)
            {
                Vector3 world = marker.transform.position + Vector3.up * 1.8f;
                Vector3 s = cam.WorldToScreenPoint(world);
                if (s.z <= 0f) continue;
                float x = Mathf.Clamp(s.x, 40f, Screen.width - 40f), y = Mathf.Clamp(Screen.height - s.y, 90f, Screen.height - 120f);
                float meters = Vector3.Distance(marker.transform.position, transform.position);
                Shadowed(new Rect(x - 50f, y - 24f, 100f, 24f), "★", markerStyle, MarkerColor);
                Shadowed(new Rect(x - 50f, y - 4f, 100f, 22f), $"{meters:0}m", markerStyle, Color.white);
            }
        }

        // ---------- 無線 ----------
        void DrawRadio()
        {
            var line = tracker ? tracker.CurrentLine : null;
            if (line == null) return;
            // 右下の体力表示に重ならない幅にする
            float width = Mathf.Clamp(Screen.width - 660f, 420f, 860f), x = (Screen.width - width) * 0.5f, y = Screen.height * 0.76f;
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(new Rect(x, y, width, 88f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            Shadowed(new Rect(x + 16f, y + 4f, width - 32f, 22f), line.speaker, titleStyle, MarkerColor);
            Shadowed(new Rect(x + 16f, y + 26f, width - 32f, 58f), line.text, radioStyle, Color.white);
        }

        // ---------- ボス ----------
        void DrawBoss()
        {
            var arena = BossArena.Instance;
            if (!arena || !arena.IsFighting) return;
            float width = Mathf.Min(640f, Screen.width - 120f), x = (Screen.width - width) * 0.5f;
            Shadowed(new Rect(0f, 30f, Screen.width, 28f), "社の主", bossStyle, new Color(1f, 0.75f, 0.6f));
            Bar(new Rect(x, 60f, width, 14f), arena.Boss.Health.Current / Mathf.Max(1f, arena.Boss.Health.Max), new Color(0.85f, 0.18f, 0.2f));
        }

        static void Shadowed(Rect rect, string text, GUIStyle style, Color color)
        {
            style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.85f);
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, style);
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
        }
    }
}
