using System.Collections.Generic;
using System.Linq;
using Matoi.Crafting;
using Matoi.Items;
using UnityEngine;

namespace Matoi.UI
{
    /// <summary>保管箱の画面。左が持ち物、右が保管箱で、選んだ物を反対側へ移す。</summary>
    public class StorageScreen : PausedScreen
    {
        Inventory inventory;
        ItemStorage storage;
        readonly int[] cursors = new int[2];
        string status = "";

        /// <summary>0 = 持ち物、1 = 保管箱。</summary>
        public int Column { get; private set; }

        IReadOnlyList<ItemStack> ListOf(int column) => column == 0 ? inventory.Stacks : storage.Stacks;

        protected override void Awake()
        {
            base.Awake();
            inventory = GetComponent<Inventory>();
            storage = GetComponent<ItemStorage>();
        }

        public override void Open()
        {
            base.Open();
            if (!IsOpen) return;
            status = "";
            Column = 0;
            cursors[0] = cursors[1] = 0;
        }

        protected override void HandleInput()
        {
            if (PressedLeft) Column = 0;
            if (PressedRight) Column = 1;
            int count = ListOf(Column).Count;
            if (count > 0)
            {
                if (PressedUp) cursors[Column] = (cursors[Column] + count - 1) % count;
                if (PressedDown) cursors[Column] = (cursors[Column] + 1) % count;
                if (PressedConfirm) MoveSelected(1);
                if (PressedAlt) MoveSelected(int.MaxValue);
            }
            if (PressedExtra) DepositMaterials();
        }

        /// <summary>選んだ物を反対側へ移す。移した数を返す。</summary>
        public int MoveSelected(int amount)
        {
            var list = ListOf(Column);
            int i = cursors[Column];
            if (i < 0 || i >= list.Count) return 0;
            var item = list[i].item;
            int moved = Column == 0 ? storage.Deposit(inventory, item, amount) : storage.Withdraw(inventory, item, amount);
            status = moved > 0 ? $"{item.displayName} ×{moved} を{(Column == 0 ? "預けた" : "取り出した")}" : "重すぎて持てません";
            cursors[Column] = Mathf.Clamp(i, 0, Mathf.Max(0, ListOf(Column).Count - 1));
            return moved;
        }

        /// <summary>持っている素材をすべて預ける。</summary>
        public int DepositMaterials()
        {
            int total = 0;
            foreach (var stack in inventory.Stacks.Where(s => s.item.category == ItemCategory.Material).ToArray())
                total += storage.Deposit(inventory, stack.item, stack.count);
            status = total > 0 ? $"素材を {total} 個預けた" : "預ける素材がありません";
            cursors[0] = Mathf.Clamp(cursors[0], 0, Mathf.Max(0, inventory.Stacks.Count - 1));
            return total;
        }

        void OnGUI()
        {
            if (!IsOpen) return;
            var panel = DrawPanel();
            float w = panel.width, h = panel.height;
            GUI.Label(new Rect(panel.x + 24f, panel.y + 14f, 200f, 40f), "保管箱", TitleStyle);
            GUI.Label(new Rect(panel.x + 170f, panel.y + 22f, w - 194f, 28f), $"持ち物の重さ {inventory.CurrentWeight:0.0} / {inventory.MaxWeight:0} kg", TextStyle);

            float columnW = (w - 64f) * 0.5f;
            DrawColumn(0, "持ち物", panel.x + 24f, panel.y + 62f, columnW, h);
            DrawColumn(1, "保管箱", panel.x + 40f + columnW, panel.y + 62f, columnW, h);

            GUI.Label(new Rect(panel.x + 24f, panel.yMax - 70f, w - 48f, 28f), status, TextStyle);
            GUI.Label(new Rect(panel.x, panel.yMax - 38f, w, 28f),
                "↑↓ 選択　←→ 左右の切替　A / Enter 1 個移す　X すべて移す　Y / T 素材をすべて預ける　B / Esc 閉じる", HintStyle);
        }

        void DrawColumn(int column, string title, float x, float y, float width, float panelHeight)
        {
            GUI.color = column == Column ? SelectedColor : Color.white;
            GUI.Label(new Rect(x, y, width, 30f), title, TextStyle);
            GUI.color = Color.white;

            var list = ListOf(column);
            float top = y + 34f, rowH = 38f;
            if (list.Count == 0) GUI.Label(new Rect(x + 4f, top, width, 30f), "何も入っていません", TextStyle);
            int visible = Mathf.Max(1, Mathf.FloorToInt((panelHeight - 180f) / rowH));
            int first = FirstVisible(cursors[column], visible, list.Count);
            for (int i = first; i < list.Count && i < first + visible; i++)
            {
                bool selected = column == Column && i == cursors[column];
                GUI.color = selected ? SelectedColor : Color.white;
                if (GUI.Button(new Rect(x, top + (i - first) * rowH, width, rowH - 4f), $"{(selected ? "▶ " : "　")}{list[i].item.displayName}  ×{list[i].count}", RowStyle))
                {
                    Column = column;
                    cursors[column] = i;
                }
            }
            GUI.color = Color.white;
        }
    }
}
