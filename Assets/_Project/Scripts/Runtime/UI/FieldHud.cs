using System.Collections.Generic;
using Matoi.Items;
using UnityEngine;

namespace Matoi.UI
{
    /// <summary>探索中の画面表示：コンパス、調べられる物の印、操作の案内、入手のお知らせ、発見の表示。</summary>
    public class FieldHud : MonoBehaviour
    {
        [SerializeField] float markerRange = 10f;
        [SerializeField] float messageTime = 4f;
        [SerializeField] float bannerTime = 3.5f;
        const int MaxMessages = 5;

        PlayerInteractor interactor;
        Inventory inventory;
        ExplorationTracker tracker;
        readonly List<(string text, float until)> messages = new List<(string, float)>();
        readonly List<Matoi.Missions.MissionMarker> objectiveMarkers = new List<Matoi.Missions.MissionMarker>();
        string banner;
        float bannerUntil;
        GUIStyle promptStyle, feedStyle, bannerStyle, compassStyle, markerStyle;

        /// <summary>今表示している操作の案内（無ければ空）。</summary>
        public string Prompt => interactor && interactor.Current ? $"[E / ↑] {interactor.Current.Label}を{interactor.Current.Verb}" : "";
        public string LastMessage => messages.Count > 0 ? messages[messages.Count - 1].text : "";
        public string Banner => banner;

        void Awake()
        {
            interactor = GetComponent<PlayerInteractor>();
            inventory = GetComponent<Inventory>();
            tracker = GetComponent<ExplorationTracker>();
        }

        void OnEnable()
        {
            if (inventory) inventory.Notified += AddMessage;
            if (tracker) tracker.Discovered += OnDiscovered;
        }

        void OnDisable()
        {
            if (inventory) inventory.Notified -= AddMessage;
            if (tracker) tracker.Discovered -= OnDiscovered;
        }

        void AddMessage(string text)
        {
            messages.Add((text, Time.unscaledTime + messageTime));
            if (messages.Count > MaxMessages) messages.RemoveAt(0);
        }

        void OnDiscovered(ExplorationPoint point)
        {
            banner = $"発見：{point.DisplayName}（{tracker.DiscoveredCount} / {tracker.TotalCount}）";
            bannerUntil = Time.unscaledTime + bannerTime;
        }

        void OnGUI()
        {
            if (MenuState.IsOpen) return;
            EnsureStyles();
            DrawCompass();
            DrawMarkers();
            DrawPrompt();
            DrawMessages();
            if (Time.unscaledTime < bannerUntil)
                Shadowed(new Rect(0f, Screen.height * 0.2f, Screen.width, 50f), banner, bannerStyle, new Color(1f, 0.88f, 0.55f));
        }

        void EnsureStyles()
        {
            if (promptStyle != null) return;
            promptStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            feedStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleLeft };
            bannerStyle = new GUIStyle(promptStyle) { fontSize = 30 };
            compassStyle = new GUIStyle(promptStyle) { fontSize = 15 };
            markerStyle = new GUIStyle(promptStyle) { fontSize = 16 };
        }

        // ---------- コンパス ----------
        void DrawCompass()
        {
            var cam = Camera.main;
            if (!cam) return;
            const float width = 420f, height = 20f, fov = 110f;
            float cx = Screen.width * 0.5f, y = 4f;
            float yaw = cam.transform.eulerAngles.y;
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(cx - width * 0.5f, y, width, height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            string[] names = { "北", "東", "南", "西" };
            for (int i = 0; i < 4; i++) DrawOnCompass(i * 90f, yaw, names[i], Color.white);
            foreach (var p in ExplorationPoint.All)
            {
                if (p.Discovered) continue;
                Vector3 d = p.transform.position - transform.position;
                DrawOnCompass(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, yaw, "◆", new Color(1f, 0.8f, 0.3f));
            }
            var home = Matoi.Crafting.BaseZone.Instance;
            if (home)
            {
                Vector3 d = home.transform.position - transform.position;
                DrawOnCompass(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, yaw, "拠", new Color(0.55f, 0.95f, 1f));
            }
            var mission = Matoi.Missions.MissionTracker.Instance;
            if (mission)
            {
                mission.CollectMarkers(objectiveMarkers);
                foreach (var marker in objectiveMarkers)
                {
                    Vector3 d = marker.transform.position - transform.position;
                    DrawOnCompass(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, yaw, "★", new Color(0.55f, 0.95f, 1f));
                }
            }

            void DrawOnCompass(float bearing, float view, string text, Color color)
            {
                float delta = Mathf.DeltaAngle(view, bearing);
                if (Mathf.Abs(delta) > fov * 0.5f) return;
                float x = cx + delta / (fov * 0.5f) * width * 0.5f;
                Shadowed(new Rect(x - 20f, y - 1f, 40f, height + 2f), text, compassStyle, color);
            }
        }

        // ---------- 調べられる物の印 ----------
        void DrawMarkers()
        {
            var cam = Camera.main;
            if (!cam) return;
            foreach (var t in Interactable.All)
            {
                if (!t || !t.CanInteract || (interactor && t == interactor.Current)) continue;
                float dist = Vector3.Distance(t.Point, transform.position);
                if (dist > markerRange) continue;
                Vector3 s = cam.WorldToScreenPoint(t.Point + Vector3.up * 0.25f);
                if (s.z <= 0f) continue;
                float alpha = Mathf.InverseLerp(markerRange, markerRange * 0.5f, dist);
                Shadowed(new Rect(s.x - 12f, Screen.height - s.y - 12f, 24f, 24f), "◇", markerStyle, new Color(1f, 0.95f, 0.75f, alpha));
            }
        }

        // ---------- 操作の案内 ----------
        void DrawPrompt()
        {
            if (!interactor || !interactor.Current) return;
            float y = Screen.height * 0.68f, cx = Screen.width * 0.5f;
            Shadowed(new Rect(0f, y, Screen.width, 32f), Prompt + (interactor.Current.HoldTime > 0f ? "（長押し）" : ""), promptStyle, Color.white);
            if (!interactor.IsHolding) return;
            const float barWidth = 220f;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(cx - barWidth * 0.5f, y + 36f, barWidth, 7f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.82f, 0.4f);
            GUI.DrawTexture(new Rect(cx - barWidth * 0.5f, y + 36f, barWidth * interactor.HoldProgress, 7f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // ---------- 入手のお知らせ ----------
        void DrawMessages()
        {
            messages.RemoveAll(m => Time.unscaledTime >= m.until);
            float y = Screen.height - 60f - messages.Count * 26f;
            foreach (var (text, until) in messages)
            {
                float alpha = Mathf.Clamp01((until - Time.unscaledTime) / 0.6f);
                Shadowed(new Rect(28f, y, 620f, 26f), text, feedStyle, new Color(1f, 1f, 1f, alpha));
                y += 26f;
            }
        }

        static void Shadowed(Rect rect, string text, GUIStyle style, Color color)
        {
            style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.8f);
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, style);
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
        }
    }
}
