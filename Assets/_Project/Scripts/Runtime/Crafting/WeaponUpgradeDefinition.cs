using System;
using UnityEngine;

namespace Matoi.Crafting
{
    public enum UpgradeKind
    {
        Damage,     // 威力
        Magazine,   // 装弾数
        Stability,  // 安定性（弾のばらつきと反動）
    }

    /// <summary>
    /// 武器強化 1 種類のデータ（全武器で共通）。段階を増やす時は levels に足すだけでよい。
    /// Project ウィンドウで右クリック → Create → Matoi → Weapon Upgrade。
    /// </summary>
    [CreateAssetMenu(menuName = "Matoi/Weapon Upgrade", fileName = "Upgrade")]
    public class WeaponUpgradeDefinition : ScriptableObject
    {
        [Serializable]
        public class Level
        {
            [Tooltip("この段階での効果の合計（0.2 なら 20%）")]
            public float bonus;
            public ItemAmount[] cost;
        }

        public UpgradeKind kind;
        public string displayName = "威力";
        [TextArea] public string description;
        public Level[] levels;

        public int MaxLevel => levels != null ? levels.Length : 0;
    }
}
