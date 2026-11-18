using Matoi.Combat;
using Matoi.InputHandling;
using Matoi.Items;
using Matoi.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Matoi.UI
{
    /// <summary>
    /// 持ち物画面。I / 十字キー右 で開く（ゲームは止まる）。
    /// 選んだ物を 使う・捨てる ことができる。Y（T キー）で主人公の選択画面へ移る。
    /// </summary>
    public class InventoryScreen : MonoBehaviour
    {
        Inventory inventory;
        Health health;
        PlayerInputReader reader;
        PlayerModelSelector selector;
        ExplorationTracker tracker;
        int cursor;
        float savedTimeScale = 1f;
        CursorLockMode savedLock;
        bool savedCursorVisible;
        string status = "";
        float resetConfirmUntil = -1f;
        GUIStyle titleStyle, rowStyle, textStyle, hintStyle;

        const float ResetConfirmSeconds = 4f;
        /// <summary>「最初からやり直す」の確認中か。</summary>
        public bool IsConfirmingReset => Time.unscaledTime < resetConfirmUntil;

        public bool IsOpen { get; private set; }

        void Awake()
        {
            inventory = GetComponent<Inventory>();
            health = GetComponent<Health>();
            reader = GetComponent<PlayerInputReader>();
            selector = GetComponent<PlayerModelSelector>();
            tracker = GetComponent<ExplorationTracker>();
            if (inventory) inventory.Notified += OnNotified;
        }

        void OnDestroy()
        {
            if (inventory) inventory.Notified -= OnNotified;
            if (IsOpen) { Time.timeScale = savedTimeScale; MenuState.IsOpen = false; }
        }

        void OnNotified(string message) { if (IsOpen) status = message; }

        void Update()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            if (!IsOpen)
            {
                bool open = (kb != null && kb.iKey.wasPressedThisFrame) || (pad != null && pad.dpad.right.wasPressedThisFrame);
                if (open && !MenuState.IsOpen) Open();
                return;
            }

            int count = inventory.Stacks.Count;
            if (count > 0)
            {
                if ((kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)) || (pad != null && pad.dpad.up.wasPressedThisFrame))
                    cursor = (cursor + count - 1) % count;
                if ((kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) || (pad != null && pad.dpad.down.wasPressedThisFrame))
                    cursor = (cursor + 1) % count;
                if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame)) UseSelected();
                if ((kb != null && kb.xKey.wasPressedThisFrame) || (pad != null && pad.buttonWest.wasPressedThisFrame)) DropSelected();
            }
            if ((kb != null && kb.backspaceKey.wasPressedThisFrame) || (pad != null && pad.selectButton.wasPressedThisFrame))
                RequestReset();
            if ((kb != null && kb.tKey.wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame))
            {
                Close();
                if (selector) selector.Open();
                return;
            }
            if ((kb != null && (kb.iKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)) || (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame)))
                Close();
        }

        public void Open()
        {
            if (IsOpen || MenuState.IsOpen || (health && health.IsDead)) return;
            IsOpen = true;
            MenuState.IsOpen = true;
            status = "";
            cursor = 0;
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
        }

        ItemDefinition Selected => cursor >= 0 && cursor < inventory.Stacks.Count ? inventory.Stacks[cursor].item : null;

        /// <summary>1 回目は確認の表示だけ。数秒以内にもう一度押すと、記録を消して最初からやり直す。</summary>
        public void RequestReset()
        {
            if (!IsConfirmingReset)
            {
                resetConfirmUntil = Time.unscaledTime + ResetConfirmSeconds;
                status = "記録を消して最初からやり直します。よければもう一度押してください";
                return;
            }
            resetConfirmUntil = -1f;
            Close();
            GetComponent<Matoi.SaveSystem.SaveManager>()?.ResetGame();
        }

        public void UseSelected()
        {
            var item = Selected;
            if (!item) return;
            if (!item.IsUsable) { status = item.category == ItemCategory.Key ? "大切な物です" : "これは素材です。拠点の作業台で使えます"; return; }
            inventory.Use(item);
            ClampCursor();
        }

        public void DropSelected()
        {
            var item = Selected;
            if (!item) return;
            if (inventory.Drop(item)) status = $"{item.displayName}を捨てた";
            ClampCursor();
        }

        void ClampCursor() => cursor = Mathf.Clamp(cursor, 0, Mathf.Max(0, inventory.Stacks.Count - 1));

        static string CategoryName(ItemCategory c) =>
            c == ItemCategory.Consumable ? "回復" : c == ItemCategory.Ammo ? "弾薬" : c == ItemCategory.Valuable ? "貴重品" : c == ItemCategory.Key ? "大切な物" : "素材";

        void OnGUI()
        {
            if (!IsOpen) return;
            titleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
            rowStyle ??= new GUIStyle(GUI.skin.button) { fontSize = 18, alignment = TextAnchor.MiddleLeft };
            textStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
            hintStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };

            float w = Mathf.Min(860f, Screen.width - 32f), h = Mathf.Min(560f, Screen.height - 32f);
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.color = new Color(0.03f, 0.03f, 0.03f, 0.88f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(panel.x + 24f, panel.y + 14f, 240f, 40f), "持ち物", titleStyle);
            string found = tracker ? $"　探索 {tracker.DiscoveredCount} / {tracker.TotalCount}" : "";
            string hp = health ? $"体力 {health.Current:0} / {health.Max:0}　" : "";
            GUI.Label(new Rect(panel.x + 200f, panel.y + 22f, w - 224f, 28f), $"{hp}重さ {inventory.CurrentWeight:0.0} / {inventory.MaxWeight:0} kg{found}", textStyle);

            float listW = w * 0.56f, top = panel.y + 64f, rowH = 38f;
            var stacks = inventory.Stacks;
            if (stacks.Count == 0) GUI.Label(new Rect(panel.x + 28f, top, listW, 30f), "何も持っていません", textStyle);
            int visible = Mathf.Max(1, Mathf.FloorToInt((h - 150f) / rowH));
            int first = Mathf.Clamp(cursor - visible + 1, 0, Mathf.Max(0, stacks.Count - visible));
            for (int i = first; i < stacks.Count && i < first + visible; i++)
            {
                var s = stacks[i];
                GUI.color = i == cursor ? new Color(1f, 0.85f, 0.5f) : Color.white;
                string mark = i == cursor ? "▶ " : "　";
                if (GUI.Button(new Rect(panel.x + 24f, top + (i - first) * rowH, listW - 24f, rowH - 4f), $"{mark}{s.item.displayName}  ×{s.count}", rowStyle)) cursor = i;
            }
            GUI.color = Color.white;

            var selected = Selected;
            float infoX = panel.x + listW + 16f, infoW = w - listW - 40f;
            if (selected)
            {
                GUI.Label(new Rect(infoX, top - 6f, infoW, 44f), selected.displayName, titleStyle);
                GUI.Label(new Rect(infoX, top + 40f, infoW, 26f), $"{CategoryName(selected.category)}　{selected.weight:0.0} kg", textStyle);
                GUI.Label(new Rect(infoX, top + 72f, infoW, 150f), selected.description, textStyle);
                if (selected.IsUsable && GUI.Button(new Rect(infoX, top + 232f, infoW * 0.48f, 40f), "使う", rowStyle)) UseSelected();
                if (GUI.Button(new Rect(infoX + infoW * 0.52f, top + 232f, infoW * 0.48f, 40f), "捨てる", rowStyle)) DropSelected();
            }
            GUI.Label(new Rect(infoX, top + 284f, infoW, 60f), status, textStyle);
            GUI.Label(new Rect(panel.x, panel.yMax - 40f, w, 30f),
                "↑↓ 選択　A / Enter 使う　X 捨てる　Y / T 主人公を選ぶ　View / BackSpace 最初から　B / I 閉じる", hintStyle);
        }
    }
}
