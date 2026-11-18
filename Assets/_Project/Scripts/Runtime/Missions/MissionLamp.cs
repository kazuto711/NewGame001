using Matoi.Combat;
using Matoi.Items;
using UnityEngine;

namespace Matoi.Missions
{
    /// <summary>社の灯籠。ボスを倒した後に火をともすと、第一話が終わる。</summary>
    public class MissionLamp : Interactable
    {
        [SerializeField] Light flame;

        public override bool CanInteract => MissionTracker.Instance && MissionTracker.Instance.IsWaitingFor(MissionSignals.LampLit);
        public override string Verb => "ともす";
        public override string Label => "灯籠";
        public override Vector3 Point => transform.position + Vector3.up * 1f;

        public void Configure(Light lampFlame) => flame = lampFlame;

        void Update()
        {
            bool lit = MissionTracker.Instance && MissionTracker.Instance.IsComplete;
            if (flame && flame.enabled != lit) flame.enabled = lit;
        }

        public override void Interact(PlayerInteractor who)
        {
            CombatFx.Instance?.PlaySound(ProceduralSfx.PowerUp, 0.7f);
            MissionSignals.Raise(MissionSignals.LampLit);
        }
    }
}
