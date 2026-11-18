using System.Collections.Generic;
using Matoi.InputHandling;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Matoi.UI
{
    /// <summary>開いている間ゲームを止める画面の共通部分（作業台・保管箱）。</summary>
    public abstract class PausedScreen : MonoBehaviour
    {
        float savedTimeScale = 1f;
        CursorLockMode savedLock;
        bool savedCursorVisible;
        int openedFrame = -1;
        PlayerInputReader reader;

        protected GUIStyle TitleStyle, RowStyle, TextStyle, HintStyle;

        public bool IsOpen { get; private set; }

        protected virtual void Awake() => reader = GetComponent<PlayerInputReader>();

        protected virtual void OnDestroy()
        {
            if (IsOpen) { Time.timeScale = savedTimeScale; MenuState.IsOpen = false; }
        }

        public virtual void Open()
        {
            if (IsOpen || MenuState.IsOpen) return;
            IsOpen = true;
            MenuState.IsOpen = true;
            openedFrame = Time.frameCount;   // 開いたボタンの押下を、画面の決定として受け取らない
            savedTimeScale = Time.timeScale;
            savedLock = Cursor.lockState;
            savedCursorVisible = Cursor.visible;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (reader) reader.enabled = false;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            MenuState.IsOpen = false;
            Time.timeScale = savedTimeScale;
            Cursor.lockState = savedLock;
            Cursor.visible = savedCursorVisible;
            if (reader) reader.enabled = true;
            OnClosed();
        }

        /// <summary>閉じた直後に呼ばれる。</summary>
        protected virtual void OnClosed() { }
        /// <summary>B / Esc で閉じられるか。</summary>
        protected virtual bool AllowCancel => true;

        protected virtual void Update()
        {
            if (!IsOpen || Time.frameCount == openedFrame) return;
            if (AllowCancel && PressedCancel) { Close(); return; }
            HandleInput();
        }

        /// <summary>縦に並んだ選択肢を描く。押された行の番号を返す（無ければ -1）。</summary>
        protected int DrawRows(IList<string> labels, int cursor, float x, float y, float width)
        {
            int pressed = -1;
            for (int i = 0; i < labels.Count; i++)
            {
                GUI.color = i == cursor ? SelectedColor : Color.white;
                if (GUI.Button(new Rect(x, y + i * 50f, width, 44f), (i == cursor ? "▶ " : "　") + labels[i], RowStyle)) pressed = i;
            }
            GUI.color = Color.white;
            return pressed;
        }

        /// <summary>画面を開いている間の操作。</summary>
        protected abstract void HandleInput();

        // ---------- ボタン ----------
        static Keyboard Kb => Keyboard.current;
        static Gamepad Pad => Gamepad.current;

        protected static bool PressedUp => (Kb != null && (Kb.upArrowKey.wasPressedThisFrame || Kb.wKey.wasPressedThisFrame)) || (Pad != null && Pad.dpad.up.wasPressedThisFrame);
        protected static bool PressedDown => (Kb != null && (Kb.downArrowKey.wasPressedThisFrame || Kb.sKey.wasPressedThisFrame)) || (Pad != null && Pad.dpad.down.wasPressedThisFrame);
        protected static bool PressedLeft => (Kb != null && (Kb.leftArrowKey.wasPressedThisFrame || Kb.aKey.wasPressedThisFrame)) || (Pad != null && Pad.dpad.left.wasPressedThisFrame);
        protected static bool PressedRight => (Kb != null && (Kb.rightArrowKey.wasPressedThisFrame || Kb.dKey.wasPressedThisFrame)) || (Pad != null && Pad.dpad.right.wasPressedThisFrame);
        protected static bool PressedConfirm => (Kb != null && (Kb.enterKey.wasPressedThisFrame || Kb.eKey.wasPressedThisFrame)) || (Pad != null && Pad.buttonSouth.wasPressedThisFrame);
        protected static bool PressedAlt => (Kb != null && Kb.xKey.wasPressedThisFrame) || (Pad != null && Pad.buttonWest.wasPressedThisFrame);
        protected static bool PressedExtra => (Kb != null && Kb.tKey.wasPressedThisFrame) || (Pad != null && Pad.buttonNorth.wasPressedThisFrame);
        protected static bool PressedCancel => (Kb != null && Kb.escapeKey.wasPressedThisFrame) || (Pad != null && Pad.buttonEast.wasPressedThisFrame);

        // ---------- 表示 ----------
        protected static readonly Color SelectedColor = new Color(1f, 0.85f, 0.5f);
        protected static readonly Color EnoughColor = new Color(0.65f, 0.95f, 0.6f);
        protected static readonly Color LackingColor = new Color(1f, 0.55f, 0.45f);

        /// <summary>画面の土台を描き、その範囲を返す。</summary>
        protected Rect DrawPanel()
        {
            TitleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
            RowStyle ??= new GUIStyle(GUI.skin.button) { fontSize = 18, alignment = TextAnchor.MiddleLeft };
            TextStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
            HintStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };

            float w = Mathf.Min(900f, Screen.width - 32f), h = Mathf.Min(580f, Screen.height - 32f);
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.color = new Color(0.03f, 0.03f, 0.03f, 0.88f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            return panel;
        }

        /// <summary>カーソルが見える範囲の先頭の行。</summary>
        protected static int FirstVisible(int cursor, int visible, int count) =>
            Mathf.Clamp(cursor - visible + 1, 0, Mathf.Max(0, count - visible));
    }
}
