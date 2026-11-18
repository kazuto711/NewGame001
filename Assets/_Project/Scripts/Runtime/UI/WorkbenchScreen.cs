using System.Collections.Generic;
using Matoi.Combat;
using Matoi.Crafting;
using Matoi.Items;
using UnityEngine;

namespace Matoi.UI
{
    /// <summary>作業台の画面。「作る」と「強化」の 2 つのページがある。</summary>
    public class WorkbenchScreen : PausedScreen
    {
        struct UpgradeRow
        {
            public WeaponDefinition weapon;
            public WeaponUpgradeDefinition upgrade;
        }

        readonly List<UpgradeRow> upgradeRows = new List<UpgradeRow>();
        PlayerCrafting crafting;
        Inventory inventory;
        int cursor;
        string status = "";

        /// <summary>0 = 作る、1 = 強化。</summary>
        public int Tab { get; private set; }
        public int Cursor => cursor;
        public int RowCount => Tab == 0 ? Recipes.Length : upgradeRows.Count;

        RecipeDefinition[] Recipes => crafting && crafting.Catalog && crafting.Catalog.recipes != null ? crafting.Catalog.recipes : System.Array.Empty<RecipeDefinition>();

        protected override void Awake()
        {
            base.Awake();
            crafting = GetComponent<PlayerCrafting>();
            inventory = GetComponent<Inventory>();
            if (inventory) inventory.Notified += OnNotified;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (inventory) inventory.Notified -= OnNotified;
        }

        void OnNotified(string message) { if (IsOpen) status = message; }

        public override void Open()
        {
            base.Open();
            if (!IsOpen) return;
            status = "";
            cursor = 0;
            Tab = 0;
            upgradeRows.Clear();
            var catalog = crafting ? crafting.Catalog : null;
            if (catalog == null || catalog.weapons == null || catalog.upgrades == null) return;
            foreach (var weapon in catalog.weapons)
                foreach (var upgrade in catalog.upgrades)
                    upgradeRows.Add(new UpgradeRow { weapon = weapon, upgrade = upgrade });
        }

        protected override void HandleInput()
        {
            if (PressedLeft || PressedRight) SelectTab(1 - Tab);
            int count = RowCount;
            if (count == 0) return;
            if (PressedUp) cursor = (cursor + count - 1) % count;
            if (PressedDown) cursor = (cursor + 1) % count;
            if (PressedConfirm) ConfirmSelected();
        }

        public void SelectTab(int tab)
        {
            Tab = Mathf.Clamp(tab, 0, 1);
            cursor = 0;
            status = "";
        }

        public void SelectRow(int row) => cursor = Mathf.Clamp(row, 0, Mathf.Max(0, RowCount - 1));

        /// <summary>選んでいる物を作る、または強化する。</summary>
        public bool ConfirmSelected()
        {
            if (RowCount == 0) return false;
            if (Tab == 0)
            {
                var recipe = Recipes[cursor];
                if (crafting.Craft(recipe)) return true;
                status = "素材が足りません";
                return false;
            }
            var row = upgradeRows[cursor];
            if (crafting.TryUpgrade(row.weapon, row.upgrade)) return true;
            status = crafting.Level(row.weapon, row.upgrade.kind) >= row.upgrade.MaxLevel ? "これ以上は強化できません" : "素材が足りません";
            return false;
        }

        void OnGUI()
        {
            if (!IsOpen) return;
            var panel = DrawPanel();
            float w = panel.width, h = panel.height;
            GUI.Label(new Rect(panel.x + 24f, panel.y + 14f, 200f, 40f), "作業台", TitleStyle);

            string[] tabs = { "作る", "強化" };
            for (int i = 0; i < tabs.Length; i++)
            {
                GUI.color = i == Tab ? SelectedColor : Color.white;
                if (GUI.Button(new Rect(panel.x + 170f + i * 124f, panel.y + 16f, 116f, 36f), tabs[i], RowStyle)) SelectTab(i);
            }
            GUI.color = Color.white;

            float listW = w * 0.52f, top = panel.y + 66f, rowH = 38f;
            int count = RowCount, visible = Mathf.Max(1, Mathf.FloorToInt((h - 120f) / rowH));
            int first = FirstVisible(cursor, visible, count);
            for (int i = first; i < count && i < first + visible; i++)
            {
                GUI.color = i == cursor ? SelectedColor : Color.white;
                string mark = i == cursor ? "▶ " : "　";
                if (GUI.Button(new Rect(panel.x + 24f, top + (i - first) * rowH, listW - 24f, rowH - 4f), mark + RowLabel(i), RowStyle)) cursor = i;
            }
            GUI.color = Color.white;

            float infoX = panel.x + listW + 16f, infoW = w - listW - 40f;
            if (count > 0) DrawDetails(infoX, top, infoW);
            GUI.Label(new Rect(infoX, panel.yMax - 100f, infoW, 56f), status, TextStyle);
            GUI.Label(new Rect(panel.x, panel.yMax - 38f, w, 28f),
                "↑↓ 選択　←→ ページ切替　A / Enter 決定　B / Esc 閉じる　素材は持ち物と保管箱の両方から使えます", HintStyle);
        }

        string RowLabel(int i)
        {
            if (Tab == 0) return Recipes[i].DisplayName;
            var row = upgradeRows[i];
            int level = crafting.Level(row.weapon, row.upgrade.kind);
            return $"{row.weapon.displayName}　{row.upgrade.displayName}　Lv{level} / {row.upgrade.MaxLevel}";
        }

        void DrawDetails(float x, float y, float width)
        {
            ItemAmount[] cost;
            float line = y;
            if (Tab == 0)
            {
                var recipe = Recipes[cursor];
                GUI.Label(new Rect(x, line, width, 36f), recipe.DisplayName, TitleStyle);
                GUI.Label(new Rect(x, line + 42f, width, 84f), recipe.result ? recipe.result.description : "", TextStyle);
                cost = recipe.ingredients;
            }
            else
            {
                var row = upgradeRows[cursor];
                int level = crafting.Level(row.weapon, row.upgrade.kind);
                bool max = level >= row.upgrade.MaxLevel;
                GUI.Label(new Rect(x, line, width, 36f), $"{row.weapon.displayName} {row.upgrade.displayName}", TitleStyle);
                string effect = max ? $"最大まで強化済み（{Percent(row.upgrade, level)}）" : $"{Percent(row.upgrade, level)} → {Percent(row.upgrade, level + 1)}";
                GUI.Label(new Rect(x, line + 42f, width, 84f), $"{row.upgrade.description}\n{effect}", TextStyle);
                cost = max ? null : row.upgrade.levels[level].cost;
            }

            line += 134f;
            if (cost == null) return;
            GUI.Label(new Rect(x, line, width, 28f), "必要な素材", TextStyle);
            line += 30f;
            foreach (var c in cost)
            {
                int have = crafting.Have(c.item);
                GUI.color = have >= c.count ? EnoughColor : LackingColor;
                GUI.Label(new Rect(x, line, width, 28f), $"{c.item.displayName}　{have} / {c.count}", TextStyle);
                line += 28f;
            }
            GUI.color = Color.white;
        }

        static string Percent(WeaponUpgradeDefinition upgrade, int level)
        {
            float bonus = level > 0 ? upgrade.levels[level - 1].bonus : 0f;
            return $"{(upgrade.kind == UpgradeKind.Stability ? "-" : "+")}{bonus * 100f:0}%";
        }
    }
}
