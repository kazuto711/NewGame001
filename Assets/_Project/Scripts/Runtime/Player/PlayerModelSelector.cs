using System;
using Matoi.Combat;
using Matoi.Equipment;
using Matoi.InputHandling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Matoi.Player
{
    /// <summary>
    /// 主人公の見た目を、ゲームの中で選べるようにする。
    /// プレイヤーには候補のモデルをすべて入れておき、起動時に選ばれた 1 体だけを残す。
    /// F4、または持ち物画面の Y ボタンで選択画面を開く。決定するとシーンを読み直す。
    /// 候補を増やす時は PlayerSetup の一覧に足すだけでよい（将来のキャラクターエディットの入口）。
    /// </summary>
    [DefaultExecutionOrder(-1000)] // 他の部品が Animator を探す前に、モデルを確定する
    public class PlayerModelSelector : MonoBehaviour
    {
        [Serializable]
        public class Variant
        {
            public string id;
            public string displayName;
            public GameObject model;
            public GameObject[] armorPieces;
            public Renderer[] hideWhenArmored;
            public float backHolsterDistance = 0.2f;
            public float thighHolsterOffset = 0.12f;
        }

        public const string PrefKey = "Matoi.PlayerModel";

        public Variant[] variants;

        int current, cursor;
        float savedTimeScale = 1f;
        CursorLockMode savedLock;
        bool savedCursorVisible;
        PlayerInputReader reader;
        GUIStyle titleStyle, itemStyle, hintStyle;

        public bool IsOpen { get; private set; }
        public Variant Current => variants != null && variants.Length > 0 ? variants[current] : null;

        void Awake()
        {
            reader = GetComponent<PlayerInputReader>();
            if (variants == null || variants.Length == 0) return;

            current = Mathf.Max(0, Array.FindIndex(variants, v => v.id == PlayerPrefs.GetString(PrefKey, "")));
            cursor = current;
            for (int i = 0; i < variants.Length; i++)
            {
                if (!variants[i].model) continue;
                variants[i].model.SetActive(i == current);
                if (i != current) Destroy(variants[i].model);   // 使わないモデルはメモリから外す
            }

            var v = variants[current];
            GetComponent<WeaponHolder>()?.SetHolsterDistances(v.backHolsterDistance, v.thighHolsterOffset);
            GetComponent<PowerArmorUser>()?.SetModelParts(v.armorPieces, v.hideWhenArmored);
        }

        void Update()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            if (!IsOpen)
            {
                if (kb != null && kb.f4Key.wasPressedThisFrame) Open();
                return;
            }

            bool prev = (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame)) || (pad != null && (pad.dpad.up.wasPressedThisFrame || pad.dpad.left.wasPressedThisFrame));
            bool next = (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame)) || (pad != null && (pad.dpad.down.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame));
            if (prev) cursor = (cursor + variants.Length - 1) % variants.Length;
            if (next) cursor = (cursor + 1) % variants.Length;

            if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame)) Choose(cursor);
            else if ((kb != null && (kb.escapeKey.wasPressedThisFrame || kb.f4Key.wasPressedThisFrame)) || (pad != null && pad.buttonEast.wasPressedThisFrame)) Close();
        }

        /// <summary>選択画面を開く（ゲームは止まる）。</summary>
        public void Open()
        {
            if (IsOpen || Matoi.UI.MenuState.IsOpen || variants == null || variants.Length < 2) return;
            IsOpen = true;
            Matoi.UI.MenuState.IsOpen = true;
            cursor = current;
            savedTimeScale = Time.timeScale;
            savedLock = Cursor.lockState;
            savedCursorVisible = Cursor.visible;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (reader) reader.enabled = false;
        }

        /// <summary>何も変えずに閉じる。</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Matoi.UI.MenuState.IsOpen = false;
            Time.timeScale = savedTimeScale;
            Cursor.lockState = savedLock;
            Cursor.visible = savedCursorVisible;
            if (reader) reader.enabled = true;
        }

        /// <summary>i 番目の主人公に決める。今と違えば保存してシーンを読み直す。</summary>
        public void Choose(int i)
        {
            bool changed = i != current;
            Close();
            if (!changed) return;
            GetComponent<Matoi.SaveSystem.SaveManager>()?.Save();   // 読み直しても持ち物などが消えないように
            PlayerPrefs.SetString(PrefKey, variants[i].id);
            PlayerPrefs.Save();
            Matoi.Missions.GameFlow.SkipTitleOnce = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        void OnDestroy()
        {
            if (IsOpen) { Time.timeScale = savedTimeScale; Matoi.UI.MenuState.IsOpen = false; }
        }

        void OnGUI()
        {
            if (!IsOpen) return;
            titleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            itemStyle ??= new GUIStyle(GUI.skin.button) { fontSize = 20 };
            hintStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = true };

            float w = 460f, h = 150f + variants.Length * 58f;
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(panel.x, panel.y + 12f, w, 40f), "主人公を選ぶ", titleStyle);

            for (int i = 0; i < variants.Length; i++)
            {
                string label = (i == cursor ? "▶ " : "") + variants[i].displayName + (i == current ? "（使用中）" : "");
                GUI.color = i == cursor ? new Color(1f, 0.85f, 0.5f) : Color.white;
                if (GUI.Button(new Rect(panel.x + 30f, panel.y + 66f + i * 58f, w - 60f, 48f), label, itemStyle)) Choose(i);
            }
            GUI.color = Color.white;
            GUI.Label(new Rect(panel.x + 20f, panel.yMax - 72f, w - 40f, 64f),
                "十字キー / 矢印で選択　A / Enter で決定　B / Esc で戻る\n別の主人公に決めると、記録してからフィールドを読み直します", hintStyle);
        }
    }
}
