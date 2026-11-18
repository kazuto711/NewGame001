using Matoi.Combat;
using UnityEngine;

namespace Matoi.Items
{
    /// <summary>薬草や廃材の山など、長押しで集める物。取り尽くすと、しばらくして元に戻る。</summary>
    public class GatherNode : Interactable
    {
        [SerializeField] string label = "薬草";
        [SerializeField] ItemDefinition item;
        [SerializeField] int minCount = 1;
        [SerializeField] int maxCount = 3;
        [SerializeField] float holdTime = 1.4f;
        [Tooltip("元に戻るまでの秒数")]
        [SerializeField] float respawnTime = 240f;

        Renderer[] renderers;
        float readyAt;

        public override bool CanInteract => Time.time >= readyAt;
        public bool IsDepleted => !CanInteract;
        public override string Verb => "採集する";
        public override string Label => label;
        public override float HoldTime => holdTime;

        public void Configure(string displayLabel, ItemDefinition definition, int min, int max, float hold)
        {
            label = displayLabel;
            item = definition;
            minCount = min;
            maxCount = max;
            holdTime = hold;
        }

        void Awake() => renderers = GetComponentsInChildren<Renderer>();

        void Update()
        {
            bool visible = CanInteract;
            if (renderers.Length > 0 && renderers[0].enabled != visible)
                foreach (var r in renderers) r.enabled = visible;
        }

        public override void Interact(PlayerInteractor who)
        {
            int amount = Random.Range(minCount, maxCount + 1);
            int added = who.Inventory.Add(item, amount);
            if (added <= 0) { who.Inventory.Notify("重すぎて持てません"); return; }
            who.Inventory.Notify($"{item.displayName} ×{added} を手に入れた");
            CombatFx.Instance?.PlaySound(ProceduralSfx.Reload, 0.35f);
            readyAt = Time.time + respawnTime;
        }
    }
}
