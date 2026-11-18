using System;
using System.Collections.Generic;
using Matoi.Combat;
using UnityEngine;

namespace Matoi.Items
{
    [Serializable]
    public class ItemStack
    {
        public ItemDefinition item;
        public int count;
        public ItemStack(ItemDefinition item, int count) { this.item = item; this.count = count; }
        public float Weight => item ? item.weight * count : 0f;
    }

    /// <summary>プレイヤーの持ち物。重さの上限があり、超える分は持てない。</summary>
    public class Inventory : MonoBehaviour
    {
        [Tooltip("持てる重さの上限 (kg)")]
        [SerializeField] float maxWeight = 30f;

        readonly List<ItemStack> stacks = new List<ItemStack>();
        Health health;
        WeaponShooter shooter;

        public IReadOnlyList<ItemStack> Stacks => stacks;
        public float MaxWeight => maxWeight;
        public float CurrentWeight { get { float w = 0f; foreach (var s in stacks) w += s.Weight; return w; } }

        /// <summary>中身が変わった時。</summary>
        public event Action Changed;
        /// <summary>画面に出す短いお知らせ。</summary>
        public event Action<string> Notified;

        void Awake()
        {
            health = GetComponent<Health>();
            shooter = GetComponent<WeaponShooter>();
        }

        public void Notify(string message) => Notified?.Invoke(message);

        public int CountOf(ItemDefinition item)
        {
            foreach (var s in stacks) if (s.item == item) return s.count;
            return 0;
        }

        /// <summary>持てるだけ持つ。実際に増えた数を返す。</summary>
        public int Add(ItemDefinition item, int count)
        {
            if (!item || count <= 0) return 0;
            int fits = item.weight > 0f ? Mathf.FloorToInt((maxWeight - CurrentWeight + 0.0001f) / item.weight) : count;
            int added = Mathf.Clamp(fits, 0, count);
            if (added <= 0) return 0;
            var stack = stacks.Find(s => s.item == item);
            if (stack != null) stack.count += added;
            else stacks.Add(new ItemStack(item, added));
            Changed?.Invoke();
            return added;
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

        /// <summary>1 個使う。効果が無い時（体力が満タンなど）は消費しない。</summary>
        public bool Use(ItemDefinition item)
        {
            if (!item || CountOf(item) <= 0 || !item.IsUsable) return false;
            if (item.heal > 0f)
            {
                if (!health || health.IsDead || health.Current >= health.Max) { Notify("体力は満タンです"); return false; }
                health.Heal(item.heal);
                Notify($"{item.displayName}を使った（体力 +{item.heal:0}）");
            }
            else
            {
                int added = shooter ? shooter.AddReserveAmmo(item.ammoFor, item.ammoAmount) : 0;
                if (added <= 0) { Notify("この弾は補充できません"); return false; }
                Notify($"{item.ammoFor.displayName} の弾 +{added}");
            }
            Remove(item, 1);
            return true;
        }

        /// <summary>1 個、足元に捨てる。</summary>
        public bool Drop(ItemDefinition item)
        {
            if (Remove(item, 1) <= 0) return false;
            WorldPickup.Spawn(item, 1, transform.position + transform.forward * 0.8f + Vector3.up * 0.5f);
            return true;
        }
    }
}
