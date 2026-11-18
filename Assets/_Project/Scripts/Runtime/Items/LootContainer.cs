using Matoi.Combat;
using UnityEngine;

namespace Matoi.Items
{
    /// <summary>木箱・タンス・廃車など、物色できる物。中身は開けた時に決まり、1 回だけ調べられる。</summary>
    public class LootContainer : Interactable
    {
        [SerializeField] string label = "木箱";
        [SerializeField] LootTable table;
        [SerializeField] float holdTime = 0.8f;
        [Tooltip("調べる位置の高さ (m)")]
        [SerializeField] float pointHeight = 0.6f;

        public bool Searched { get; private set; }
        public override bool CanInteract => !Searched;
        public override string Verb => "物色する";
        public override string Label => label;
        public override float HoldTime => holdTime;
        public override Vector3 Point => transform.position + Vector3.up * pointHeight;
        public override Transform SightRoot => sightRoot ? sightRoot : transform;

        [Tooltip("本体が別にある時の本体（廃車など）")]
        [SerializeField] Transform sightRoot;

        public void Configure(string displayLabel, LootTable loot, float hold, float height, Transform body = null)
        {
            label = displayLabel;
            table = loot;
            holdTime = hold;
            pointHeight = height;
            sightRoot = body;
        }

        public override void Interact(PlayerInteractor who)
        {
            Searched = true;
            var loot = table ? table.Roll() : null;
            if (loot == null || loot.Count == 0) { who.Inventory.Notify($"{label}は空だった"); return; }
            CombatFx.Instance?.PlaySound(ProceduralSfx.Reload, 0.4f);
            foreach (var stack in loot)
            {
                int added = who.Inventory.Add(stack.item, stack.count);
                if (added > 0) who.Inventory.Notify($"{stack.item.displayName} ×{added} を手に入れた");
                int left = stack.count - added;
                if (left <= 0) continue;
                // 持ちきれない分は足元に置いておく
                WorldPickup.Spawn(stack.item, left, who.transform.position + who.transform.right * 0.7f + Vector3.up * 0.5f);
                who.Inventory.Notify($"{stack.item.displayName} は重くて持てず、足元に置いた");
            }
        }
    }
}
