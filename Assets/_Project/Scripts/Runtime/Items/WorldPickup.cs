using Matoi.Combat;
using UnityEngine;

namespace Matoi.Items
{
    /// <summary>地面に落ちている物。調べると持ち物に入る。</summary>
    public class WorldPickup : Interactable
    {
        [SerializeField] ItemDefinition item;
        [SerializeField] int count = 1;

        public ItemDefinition Item => item;
        public int Count => count;
        public override string Verb => "拾う";
        public override string Label => count > 1 ? $"{item.displayName} ×{count}" : item.displayName;

        public void Configure(ItemDefinition definition, int amount)
        {
            item = definition;
            count = Mathf.Max(1, amount);
        }

        /// <summary>セーブデータから残りの個数を戻す。</summary>
        public void RestoreCount(int amount) => count = Mathf.Max(1, amount);

        public override void Interact(PlayerInteractor who)
        {
            int added = who.Inventory.Add(item, count);
            if (added <= 0) { who.Inventory.Notify("重すぎて持てません"); return; }
            who.Inventory.Notify($"{item.displayName} ×{added} を手に入れた");
            CombatFx.Instance?.PlaySound(ProceduralSfx.Reload, 0.35f);
            count -= added;
            if (count <= 0) Destroy(gameObject);
            else who.Inventory.Notify("重すぎて全部は持てません");
        }

        /// <summary>地面の上に物を出す（捨てた時・持ちきれなかった時）。</summary>
        public static WorldPickup Spawn(ItemDefinition item, int count, Vector3 near)
        {
            Vector3 p = near;
            if (Physics.Raycast(near, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point;
            var go = new GameObject("Pickup_" + item.name);
            go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            if (item.worldModel) Instantiate(item.worldModel, go.transform, false);
            var pickup = go.AddComponent<WorldPickup>();
            pickup.Configure(item, count);
            return pickup;
        }
    }
}
