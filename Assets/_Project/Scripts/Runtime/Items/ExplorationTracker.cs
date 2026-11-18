using System;
using Matoi.Combat;
using UnityEngine;

namespace Matoi.Items
{
    /// <summary>プレイヤーが探索地点に近づいたかを調べる。</summary>
    public class ExplorationTracker : MonoBehaviour
    {
        public int DiscoveredCount { get; private set; }
        public int TotalCount => ExplorationPoint.All.Count;
        /// <summary>新しい場所を見つけた時。</summary>
        public event Action<ExplorationPoint> Discovered;

        /// <summary>セーブデータから戻した後に、発見した数を数え直す。</summary>
        public void RecountDiscovered()
        {
            DiscoveredCount = 0;
            foreach (var p in ExplorationPoint.All) if (p.Discovered) DiscoveredCount++;
        }

        void Update()
        {
            foreach (var p in ExplorationPoint.All)
            {
                if (p.Discovered) continue;
                Vector3 d = p.transform.position - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > p.Radius * p.Radius) continue;
                p.Discovered = true;
                DiscoveredCount++;
                CombatFx.Instance?.PlaySound(ProceduralSfx.PowerUp, 0.35f);
                Discovered?.Invoke(p);
            }
        }
    }
}
