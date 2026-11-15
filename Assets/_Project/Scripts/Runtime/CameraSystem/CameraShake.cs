using Unity.Cinemachine;
using UnityEngine;

namespace Matoi.CameraSystem
{
    /// <summary>
    /// 画面揺れの窓口。どこからでも CameraShake.Shake(強さ) で呼べる。
    /// 銃の反動・被弾・パワーアーマーの着地などで使う（M5 以降）。
    /// </summary>
    [RequireComponent(typeof(CinemachineImpulseSource))]
    public class CameraShake : MonoBehaviour
    {
        static CameraShake instance;
        CinemachineImpulseSource source;

        void Awake()
        {
            instance = this;
            source = GetComponent<CinemachineImpulseSource>();
        }

        void OnDestroy() { if (instance == this) instance = null; }

        /// <param name="strength">0.1=軽い反動, 0.5=爆発, 1=大きな衝撃</param>
        public static void Shake(float strength)
        {
            if (instance == null || instance.source == null) return;
            instance.source.GenerateImpulseWithForce(strength);
        }
    }
}
