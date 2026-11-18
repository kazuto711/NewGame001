using Matoi.Items;
using Matoi.Missions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Matoi.UI
{
    /// <summary>第一話の終わりの画面。閉じると、そのままフィールドを探索できる。</summary>
    public class EndingScreen : PausedScreen
    {
        static readonly string[] Labels = { "探索を続ける", "タイトルへ戻る" };

        [Tooltip("最後の無線を聞かせてから画面を出すまでの秒数")]
        [SerializeField] float delay = 7f;

        MissionTracker tracker;
        ExplorationTracker exploration;
        float openAt = -1f;
        int cursor;
        GUIStyle bigStyle;

        protected override bool AllowCancel => false;
        /// <summary>終わりの画面が出るのを待っているか。</summary>
        public bool IsPending => openAt > 0f;

        protected override void Awake()
        {
            base.Awake();
            tracker = GetComponent<MissionTracker>();
            exploration = GetComponent<ExplorationTracker>();
        }

        void OnEnable() { if (tracker) tracker.Completed += Schedule; }
        void OnDisable() { if (tracker) tracker.Completed -= Schedule; }

        void Schedule() => openAt = Time.time + delay;

        protected override void Update()
        {
            if (openAt > 0f && Time.time >= openAt && !MenuState.IsOpen)
            {
                openAt = -1f;
                Open();
            }
            base.Update();
        }

        protected override void HandleInput()
        {
            if (PressedUp || PressedDown) cursor = 1 - cursor;
            if (PressedConfirm) Select(cursor);
        }

        public void Select(int row)
        {
            Close();
            if (row == 1) SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        void OnGUI()
        {
            if (!IsOpen) return;
            bigStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            RowStyle ??= new GUIStyle(GUI.skin.button) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
            TextStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 19, wordWrap = true, alignment = TextAnchor.MiddleCenter };

            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float top = Screen.height * 0.18f;
            GUI.Label(new Rect(0f, top, Screen.width, 70f), tracker && tracker.Mission ? $"{tracker.Mission.title}　完" : "完", bigStyle);
            string text = tracker && tracker.Mission ? tracker.Mission.endingText : "";
            GUI.Label(new Rect(Screen.width * 0.5f - 340f, top + 84f, 680f, 110f), text, TextStyle);
            if (exploration) GUI.Label(new Rect(0f, top + 200f, Screen.width, 32f), $"発見した場所　{exploration.DiscoveredCount} / {exploration.TotalCount}", TextStyle);

            int pressed = DrawRows(Labels, cursor, Screen.width * 0.5f - 160f, top + 256f, 320f);
            if (pressed >= 0) Select(pressed);
        }
    }
}
