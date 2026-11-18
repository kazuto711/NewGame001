using Matoi.CameraSystem;
using Matoi.Combat;
using Matoi.Missions;
using Matoi.SaveSystem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Matoi.UI
{
    /// <summary>一時停止メニュー。Esc / Start で開く。</summary>
    public class PauseMenu : PausedScreen
    {
        static readonly string[] Labels = { "ゲームに戻る", "操作の説明", "記録してタイトルへ", "記録して終了" };

        const string HelpText =
            "移動　　　　　WASD / 左スティック\n" +
            "カメラ　　　　マウス / 右スティック\n" +
            "ダッシュ　　　左 Shift / 左スティック押し込み\n" +
            "ジャンプ　　　Space / A\n" +
            "回避　　　　　左 Alt / LB\n" +
            "しゃがむ　　　C / 十字キー左\n" +
            "武器を構える　Q / Y（狙う：右クリック / LT）\n" +
            "撃つ　　　　　左クリック / RT\n" +
            "殴る　　　　　F / B\n" +
            "リロード　　　R / X\n" +
            "武器の切替　　Tab / RB\n" +
            "調べる　　　　E / 十字キー上\n" +
            "持ち物　　　　I / 十字キー右\n" +
            "視点の切替　　F3 / 十字キー下";

        int cursor;
        bool showingHelp;
        Health health;

        public bool ShowingHelp => showingHelp;

        protected override void Awake()
        {
            base.Awake();
            health = GetComponent<Health>();
        }

        protected override void Update()
        {
            if (!IsOpen)
            {
                bool pressed = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                               || (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
                if (pressed && !MenuState.IsOpen && MenuState.ClosedFrame != Time.frameCount && !(health && health.IsDead)) Open();
                return;
            }
            base.Update();
        }

        public override void Open()
        {
            base.Open();
            cursor = 0;
            showingHelp = false;
        }

        /// <summary>三人称視点に戻る時は、カーソルを画面に固定し直す。</summary>
        protected override void OnClosed()
        {
            if (Application.isBatchMode) return;
            bool thirdPerson = CameraModeSwitcher.Mode == CameraMode.ThirdPerson;
            Cursor.lockState = thirdPerson ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !thirdPerson;
        }

        protected override void HandleInput()
        {
            if (showingHelp)
            {
                if (PressedConfirm) showingHelp = false;
                return;
            }
            if (PressedUp) cursor = (cursor + Labels.Length - 1) % Labels.Length;
            if (PressedDown) cursor = (cursor + 1) % Labels.Length;
            if (PressedConfirm) Select(cursor);
        }

        public void Select(int row)
        {
            var save = GetComponent<SaveManager>();
            switch (row)
            {
                case 0: Close(); break;
                case 1: showingHelp = true; break;
                case 2:
                    save?.Save();
                    Close();
                    SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                    break;
                case 3:
                    save?.Save();
                    Application.Quit();
                    break;
            }
        }

        void OnGUI()
        {
            if (!IsOpen) return;
            var panel = DrawPanel();
            GUI.Label(new Rect(panel.x + 24f, panel.y + 14f, panel.width - 48f, 40f), showingHelp ? "操作の説明" : "一時停止", TitleStyle);
            if (showingHelp)
            {
                GUI.Label(new Rect(panel.x + 40f, panel.y + 66f, panel.width - 80f, panel.height - 120f), HelpText, TextStyle);
                if (GUI.Button(new Rect(panel.x + 24f, panel.yMax - 54f, 180f, 40f), "戻る", RowStyle)) showingHelp = false;
                return;
            }
            int pressed = DrawRows(Labels, cursor, panel.x + panel.width * 0.5f - 180f, panel.y + 120f, 360f);
            if (pressed >= 0) { cursor = pressed; Select(pressed); }
            GUI.Label(new Rect(panel.x, panel.yMax - 38f, panel.width, 28f), "↑↓ 選択　A / Enter 決定　B / Esc 戻る", HintStyle);
        }
    }
}
