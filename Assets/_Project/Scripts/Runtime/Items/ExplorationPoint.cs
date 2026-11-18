using System.Collections.Generic;
using UnityEngine;

namespace Matoi.Items
{
    /// <summary>探索の目的地。近づくと「発見」になる。</summary>
    public class ExplorationPoint : MonoBehaviour
    {
        static readonly List<ExplorationPoint> all = new List<ExplorationPoint>();
        public static IReadOnlyList<ExplorationPoint> All => all;

        [SerializeField] string displayName = "場所";
        [SerializeField] float radius = 7f;

        public string DisplayName => displayName;
        public float Radius => radius;
        public bool Discovered { get; set; }

        public void Configure(string title, float discoverRadius)
        {
            displayName = title;
            radius = discoverRadius;
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);
    }
}
