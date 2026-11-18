using System.Collections.Generic;
using Matoi.Missions;
using Matoi.SaveSystem;
using UnityEngine;

namespace Matoi.UI
{
    /// <summary>タイトル画面。起動した時に、はじめから・つづきから・終了を選ぶ。</summary>
    public class TitleScreen : PausedScreen
    {
        public enum Choice { NewGame, Continue, Quit }

        const float ConfirmSeconds = 4f;

        readonly List<Choice> choices = new List<Choice>();
        readonly List<string> labels = new List<string>();
        int cursor;
        float confirmUntil = -1f;
        string status = "";
        GUIStyle logoStyle, subStyle;
        SaveManager save;

        protected override bool AllowCancel => false;
        public IReadOnlyList<Choice> Choices => choices;
        public int Cursor => cursor;

        protected override void Awake()
        {
            base.Awake();
            save = GetComponent<SaveManager>();
        }

        void Start()
        {
            if (GameFlow.ConsumeShowTitle()) Open();
        }

        public override void Open()
        {
            base.Open();
            if (!IsOpen) return;
            choices.Clear();
            labels.Clear();
            if (SaveManager.HasSave) Add(Choice.Continue, "つづきから");
            Add(Choice.NewGame, "はじめから");
            Add(Choice.Quit, "ゲームを終了");
            cursor = 0;
            status = "";
            confirmUntil = -1f;
        }

        void Add(Choice choice, string label)
        {
            choices.Add(choice);
            labels.Add(label);
        }

        protected override void HandleInput()
        {
            if (PressedUp) cursor = (cursor + choices.Count - 1) % choices.Count;
            if (PressedDown) cursor = (cursor + 1) % choices.Count;
            if (PressedConfirm) Select(choices[cursor]);
        }

        public void Select(Choice choice)
        {
            switch (choice)
            {
                case Choice.Continue: Close(); break;
                case Choice.NewGame: NewGame(); break;
                case Choice.Quit: Application.Quit(); break;
            }
        }

        /// <summary>記録がある時は、もう一度押して確認してから最初に戻す。</summary>
        void NewGame()
        {
            if (SaveManager.HasSave && Time.unscaledTime >= confirmUntil)
            {
                confirmUntil = Time.unscaledTime + ConfirmSeconds;
                status = "今の記録を消して始めます。よければもう一度押してください";
                return;
            }
            Close();
            save.ResetGame();
        }

        void OnGUI()
        {
            if (!IsOpen) return;
            logoStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 86, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            subStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            RowStyle ??= new GUIStyle(GUI.skin.button) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
            TextStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true, alignment = TextAnchor.MiddleCenter };

            GUI.color = new Color(0.02f, 0.02f, 0.03f, 0.72f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float cx = Screen.width * 0.5f, top = Screen.height * 0.16f;
            GUI.Label(new Rect(0f, top, Screen.width, 120f), "纏", logoStyle);
            GUI.Label(new Rect(0f, top + 112f, Screen.width, 36f), "- MATOI -", subStyle);
            GUI.Label(new Rect(0f, top + 150f, Screen.width, 32f), "第一話　社の灯", subStyle);

            const float width = 320f;
            float rowsY = top + 214f;
            int pressed = DrawRows(labels, cursor, cx - width * 0.5f, rowsY, width);
            if (pressed >= 0) { cursor = pressed; Select(choices[pressed]); }
            GUI.Label(new Rect(cx - 320f, rowsY + choices.Count * 50f + 10f, 640f, 40f), status, TextStyle);
        }
    }
}
