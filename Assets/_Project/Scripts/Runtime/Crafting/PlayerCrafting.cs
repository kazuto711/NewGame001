using System.Collections.Generic;
using Matoi.Combat;
using Matoi.Items;
using UnityEngine;

namespace Matoi.Crafting
{
    /// <summary>
    /// プレイヤーのクラフトと武器強化。素材は持ち物と保管箱の両方から使える。
    /// 強化の段階はここで覚えておき、武器のデータ（WeaponDefinition）は書き換えない。
    /// </summary>
    public class PlayerCrafting : MonoBehaviour
    {
        [SerializeField] GameCatalog catalog;

        readonly Dictionary<(WeaponDefinition weapon, UpgradeKind kind), int> levels = new Dictionary<(WeaponDefinition, UpgradeKind), int>();
        Inventory inventory;
        ItemStorage storage;

        public GameCatalog Catalog => catalog;
        /// <summary>何かを作った時。</summary>
        public event System.Action<RecipeDefinition> Crafted;

        void Awake()
        {
            inventory = GetComponent<Inventory>();
            storage = GetComponent<ItemStorage>();
        }

        // ---------- 素材 ----------
        /// <summary>持ち物と保管箱を合わせた個数。</summary>
        public int Have(ItemDefinition item) => (inventory ? inventory.CountOf(item) : 0) + (storage ? storage.CountOf(item) : 0);

        public bool HasAll(ItemAmount[] cost)
        {
            if (cost == null) return true;
            foreach (var c in cost)
                if (Have(c.item) < c.count) return false;
            return true;
        }

        /// <summary>素材を消費する。身軽でいられるよう、保管箱の分から先に使う。</summary>
        void Spend(ItemAmount[] cost)
        {
            if (cost == null) return;
            foreach (var c in cost)
            {
                int left = c.count - (storage ? storage.Remove(c.item, c.count) : 0);
                if (left > 0 && inventory) inventory.Remove(c.item, left);
            }
        }

        // ---------- クラフト ----------
        public bool CanCraft(RecipeDefinition recipe) => recipe && recipe.result && HasAll(recipe.ingredients);

        /// <summary>作る。持ちきれない分は保管箱へ入れる。</summary>
        public bool Craft(RecipeDefinition recipe)
        {
            if (!CanCraft(recipe)) return false;
            Spend(recipe.ingredients);
            int added = inventory ? inventory.Add(recipe.result, recipe.resultCount) : 0;
            int left = recipe.resultCount - added;
            if (left > 0 && storage) storage.Add(recipe.result, left);
            CombatFx.Instance?.PlaySound(ProceduralSfx.Reload, 0.5f);
            inventory?.Notify(left > 0 ? $"{recipe.DisplayName}を作った（重いので保管箱へ入れた）" : $"{recipe.DisplayName}を作った");
            Crafted?.Invoke(recipe);
            return true;
        }

        // ---------- 武器強化 ----------
        public int Level(WeaponDefinition weapon, UpgradeKind kind) => levels.TryGetValue((weapon, kind), out int level) ? level : 0;

        /// <summary>セーブデータから段階を戻す。</summary>
        public void SetLevel(WeaponDefinition weapon, UpgradeKind kind, int level)
        {
            var upgrade = catalog ? catalog.UpgradeOf(kind) : null;
            levels[(weapon, kind)] = Mathf.Clamp(level, 0, upgrade ? upgrade.MaxLevel : 0);
        }

        /// <summary>強化済みの段階の一覧（セーブ用）。</summary>
        public IEnumerable<(WeaponDefinition weapon, UpgradeKind kind, int level)> AllLevels()
        {
            foreach (var pair in levels)
                if (pair.Value > 0) yield return (pair.Key.weapon, pair.Key.kind, pair.Value);
        }

        public bool CanUpgrade(WeaponDefinition weapon, WeaponUpgradeDefinition upgrade)
        {
            if (!weapon || !upgrade) return false;
            int level = Level(weapon, upgrade.kind);
            return level < upgrade.MaxLevel && HasAll(upgrade.levels[level].cost);
        }

        public bool TryUpgrade(WeaponDefinition weapon, WeaponUpgradeDefinition upgrade)
        {
            if (!CanUpgrade(weapon, upgrade)) return false;
            int level = Level(weapon, upgrade.kind);
            Spend(upgrade.levels[level].cost);
            levels[(weapon, upgrade.kind)] = level + 1;
            CombatFx.Instance?.PlaySound(ProceduralSfx.PowerUp, 0.5f);
            inventory?.Notify($"{weapon.displayName}の{upgrade.displayName}が Lv{level + 1} になった");
            return true;
        }

        /// <summary>今の段階の効果（0.2 なら 20%）。</summary>
        public float Bonus(WeaponDefinition weapon, UpgradeKind kind)
        {
            int level = Level(weapon, kind);
            var upgrade = catalog ? catalog.UpgradeOf(kind) : null;
            return level > 0 && upgrade ? upgrade.levels[level - 1].bonus : 0f;
        }

        public float DamageScale(WeaponDefinition weapon) => 1f + Bonus(weapon, UpgradeKind.Damage);
        public int MagazineSize(WeaponDefinition weapon) => Mathf.RoundToInt(weapon.magazineSize * (1f + Bonus(weapon, UpgradeKind.Magazine)));
        /// <summary>弾のばらつきと反動に掛ける倍率。</summary>
        public float StabilityScale(WeaponDefinition weapon) => Mathf.Max(0.05f, 1f - Bonus(weapon, UpgradeKind.Stability));
    }
}
