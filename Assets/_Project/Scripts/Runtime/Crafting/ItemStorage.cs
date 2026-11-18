using System;
using System.Collections.Generic;
using Matoi.Items;
using UnityEngine;

namespace Matoi.Crafting
{
    /// <summary>拠点の保管箱の中身。重さの制限はない。</summary>
    public class ItemStorage : MonoBehaviour
    {
        readonly List<ItemStack> stacks = new List<ItemStack>();

        public IReadOnlyList<ItemStack> Stacks => stacks;
        public event Action Changed;

        public int CountOf(ItemDefinition item)
        {
            foreach (var s in stacks) if (s.item == item) return s.count;
            return 0;
        }

        public void Add(ItemDefinition item, int count)
        {
            if (!item || count <= 0) return;
            var stack = stacks.Find(s => s.item == item);
            if (stack != null) stack.count += count;
            else stacks.Add(new ItemStack(item, count));
            Changed?.Invoke();
        }

        /// <summary>減らす。実際に減った数を返す。</summary>
        public int Remove(ItemDefinition item, int count)
        {
            var stack = stacks.Find(s => s.item == item);
            if (stack == null || count <= 0) return 0;
            int removed = Mathf.Min(stack.count, count);
            stack.count -= removed;
            if (stack.count <= 0) stacks.Remove(stack);
            Changed?.Invoke();
            return removed;
        }

        /// <summary>持ち物から預ける。預けた数を返す。</summary>
        public int Deposit(Inventory inventory, ItemDefinition item, int count)
        {
            int moved = inventory.Remove(item, count);
            Add(item, moved);
            return moved;
        }

        /// <summary>持ち物へ引き出す。重さの上限まで移し、移した数を返す。</summary>
        public int Withdraw(Inventory inventory, ItemDefinition item, int count)
        {
            int moved = inventory.Add(item, Mathf.Min(count, CountOf(item)));
            Remove(item, moved);
            return moved;
        }

        /// <summary>セーブデータから中身を戻す。</summary>
        public void SetContents(IEnumerable<ItemStack> contents)
        {
            stacks.Clear();
            foreach (var s in contents)
                if (s.item && s.count > 0) stacks.Add(new ItemStack(s.item, s.count));
            Changed?.Invoke();
        }
    }
}
