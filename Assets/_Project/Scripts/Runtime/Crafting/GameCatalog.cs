using Matoi.Combat;
using Matoi.Items;
using UnityEngine;

namespace Matoi.Crafting
{
    /// <summary>
    /// ゲームに登場するアイテム・レシピ・強化・武器の一覧。
    /// 作業台の画面と、セーブデータの読み込み（名前から実物を探す）に使う。
    /// </summary>
    [CreateAssetMenu(menuName = "Matoi/Game Catalog", fileName = "GameCatalog")]
    public class GameCatalog : ScriptableObject
    {
        public ItemDefinition[] items;
        public RecipeDefinition[] recipes;
        public WeaponUpgradeDefinition[] upgrades;
        public WeaponDefinition[] weapons;

        public ItemDefinition FindItem(string assetName) => Find(items, assetName);
        public WeaponDefinition FindWeapon(string assetName) => Find(weapons, assetName);
        public WeaponUpgradeDefinition FindUpgrade(string assetName) => Find(upgrades, assetName);

        public WeaponUpgradeDefinition UpgradeOf(UpgradeKind kind)
        {
            if (upgrades != null)
                foreach (var u in upgrades)
                    if (u && u.kind == kind) return u;
            return null;
        }

        static T Find<T>(T[] list, string assetName) where T : Object
        {
            if (list == null || string.IsNullOrEmpty(assetName)) return null;
            foreach (var entry in list)
                if (entry && entry.name == assetName) return entry;
            return null;
        }
    }
}
