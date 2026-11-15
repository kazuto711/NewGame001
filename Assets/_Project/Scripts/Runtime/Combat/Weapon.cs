using UnityEngine;

namespace Matoi.Combat
{
    /// <summary>
    /// 武器の見た目（プレハブ）に付ける。原点＝握る位置、+Z＝銃口の向き。
    /// </summary>
    public class Weapon : MonoBehaviour
    {
        [Tooltip("銃口の位置（弾・マズルフラッシュの発生位置）")]
        [SerializeField] Transform muzzle;
        [Tooltip("左手で支える位置（ライフルなど。無ければ片手扱い）")]
        [SerializeField] Transform supportHand;

        public Transform Muzzle => muzzle ? muzzle : transform;
        public Transform SupportHand => supportHand;

        void Reset()
        {
            foreach (var t in GetComponentsInChildren<Transform>())
            {
                if (t.name == "Muzzle") muzzle = t;
                if (t.name == "SupportHand") supportHand = t;
            }
        }
    }
}
