using Matoi.Combat;
using Matoi.Items;
using Matoi.Player;
using Matoi.SaveSystem;
using Matoi.UI;
using UnityEngine;

namespace Matoi.Crafting
{
    /// <summary>拠点の設備（作業台・保管箱・寝床）。「調べる」ボタンで使う。</summary>
    public class BaseStation : Interactable
    {
        public enum Kind { Workbench, Storage, Bed }

        [SerializeField] Kind kind;
        [SerializeField] string label = "作業台";
        [Tooltip("調べる位置の高さ (m)")]
        [SerializeField] float pointHeight = 0.7f;

        public Kind StationKind => kind;
        public override string Verb => kind == Kind.Storage ? "開く" : "使う";
        public override string Label => label;
        public override Vector3 Point => transform.position + Vector3.up * pointHeight;

        public void Configure(Kind stationKind, string displayLabel, float height)
        {
            kind = stationKind;
            label = displayLabel;
            pointHeight = height;
        }

        public override void Interact(PlayerInteractor who)
        {
            switch (kind)
            {
                case Kind.Workbench: who.GetComponent<WorkbenchScreen>()?.Open(); break;
                case Kind.Storage: who.GetComponent<StorageScreen>()?.Open(); break;
                case Kind.Bed: Rest(who); break;
            }
        }

        /// <summary>休む：体力を全回復し、ここを復活地点にして記録する。</summary>
        static void Rest(PlayerInteractor who)
        {
            var health = who.GetComponent<Health>();
            if (health) health.Heal(health.Max);
            who.GetComponent<PlayerLife>()?.SetRespawnPoint(who.transform.position, who.transform.eulerAngles.y);
            var save = who.GetComponent<SaveManager>();
            bool saved = save && save.Save();
            CombatFx.Instance?.PlaySound(ProceduralSfx.PowerUp, 0.4f);
            who.Inventory.Notify(saved ? "休んで体力が回復した（記録しました）" : "休んで体力が回復した（記録できませんでした）");
            Matoi.Missions.MissionSignals.Raise(Matoi.Missions.MissionSignals.Rest);
        }
    }
}
