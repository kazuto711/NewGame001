using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Matoi.Equipment
{
    /// <summary>
    /// 置いてある（誰も乗っていない）パワーアーマー。近づいて E / ↑ で乗り込む。
    /// 耐久値はアーマー側が持っていて、乗っていない間に少しずつ自己修復する。
    /// </summary>
    public class PowerArmorStation : MonoBehaviour
    {
        static readonly List<PowerArmorStation> all = new List<PowerArmorStation>();
        public static IReadOnlyList<PowerArmorStation> All => all;

        [SerializeField] GameObject visual;
        [SerializeField] Animator visualAnimator;
        [SerializeField] Collider blocker;

        [Header("耐久")]
        public float maxIntegrity = 300f;
        [Tooltip("乗っていない間の自己修復（毎秒）")]
        public float repairPerSecond = 4f;
        [Tooltip("乗り込める距離 (m)")]
        public float interactRadius = 2.4f;

        public float Integrity { get; set; }
        public bool Occupied { get; private set; }
        public float IntegrityFraction => Integrity / Mathf.Max(1f, maxIntegrity);

        void Awake() => Integrity = maxIntegrity;
        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        IEnumerator Start()
        {
            // 中身の無いアーマーなので、待機の姿勢で止めておく（呼吸のような揺れを出さない）
            if (!visualAnimator) yield break;
            yield return null;
            yield return null;
            visualAnimator.speed = 0f;
        }

        void Update()
        {
            if (!Occupied) Integrity = Mathf.Min(maxIntegrity, Integrity + repairPerSecond * Time.deltaTime);
        }

        public void SetOccupied(bool value)
        {
            Occupied = value;
            if (visual) visual.SetActive(!value);
            if (blocker) blocker.enabled = !value;
        }

        /// <summary>降りた場所に置き直す。</summary>
        public void ParkAt(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
        }

        public bool CanEnter(Vector3 from) =>
            !Occupied && Integrity > maxIntegrity * 0.1f &&
            Vector3.Distance(Flat(from), Flat(transform.position)) <= interactRadius;

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
    }
}
