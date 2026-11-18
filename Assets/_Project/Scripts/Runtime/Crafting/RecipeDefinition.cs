using System;
using Matoi.Items;
using UnityEngine;

namespace Matoi.Crafting
{
    /// <summary>アイテムとその個数の組み合わせ（必要な素材など）。</summary>
    [Serializable]
    public struct ItemAmount
    {
        public ItemDefinition item;
        public int count;
        public ItemAmount(ItemDefinition item, int count) { this.item = item; this.count = count; }
    }

    /// <summary>
    /// 作業台で作れる物 1 種類のデータ。増やす時はこのファイルを複製して値を変える。
    /// Project ウィンドウで右クリック → Create → Matoi → Recipe。
    /// </summary>
    [CreateAssetMenu(menuName = "Matoi/Recipe", fileName = "Recipe")]
    public class RecipeDefinition : ScriptableObject
    {
        public ItemDefinition result;
        [Tooltip("1 回で作れる個数")]
        public int resultCount = 1;
        public ItemAmount[] ingredients;

        public string DisplayName => result ? (resultCount > 1 ? $"{result.displayName} ×{resultCount}" : result.displayName) : name;
    }
}
