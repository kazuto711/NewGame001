using System.Collections.Generic;
using UnityEngine;

namespace Matoi.Missions
{
    /// <summary>目的地の目印。同じグループの目印が、今の目的の間だけコンパスと画面に出る。</summary>
    public class MissionMarker : MonoBehaviour
    {
        static readonly List<MissionMarker> all = new List<MissionMarker>();
        public static IReadOnlyList<MissionMarker> All => all;

        [SerializeField] string group;

        public string Group => group;
        public void Configure(string markerGroup) => group = markerGroup;

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        /// <summary>そのグループの最初の目印。</summary>
        public static MissionMarker Find(string markerGroup)
        {
            foreach (var marker in all)
                if (marker.group == markerGroup) return marker;
            return null;
        }
    }
}
