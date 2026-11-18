using System;
using System.Collections.Generic;
using UnityEngine;

namespace Matoi.Items
{
    /// <summary>物色した時に出てくる物の一覧。中身は項目ごとの確率で決まる。</summary>
    [CreateAssetMenu(menuName = "Matoi/Loot Table", fileName = "Loot")]
    public class LootTable : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public ItemDefinition item;
            [Range(0f, 1f)] public float chance = 0.5f;
            public int min = 1;
            public int max = 1;
        }

        public Entry[] entries;
        [Tooltip("空振りの確率（0 なら必ず何か入っている）")]
        [Range(0f, 1f)] public float emptyChance;

        /// <summary>中身を決める。</summary>
        public List<ItemStack> Roll()
        {
            var result = new List<ItemStack>();
            if (entries == null || entries.Length == 0 || UnityEngine.Random.value < emptyChance) return result;
            foreach (var e in entries)
                if (e.item && UnityEngine.Random.value <= e.chance)
                    result.Add(new ItemStack(e.item, UnityEngine.Random.Range(e.min, e.max + 1)));
            if (result.Count == 0)
            {
                var e = entries[UnityEngine.Random.Range(0, entries.Length)];
                if (e.item) result.Add(new ItemStack(e.item, Mathf.Max(1, e.min)));
            }
            return result;
        }
    }
}
