using System;
using UnityEngine;

namespace Matoi.Combat
{
    /// <summary>
    /// 武器を「持つ・しまう・構える・持ち替える」を担当する。
    ///  - しまっている時：拳銃は右太もも、ライフルは背中
    ///  - 持っている時：上半身を武器の構えアニメーションにする
    ///      拳銃   … 手首に付ける
    ///      ライフル … 右肩に銃床を当てる位置に置き、両腕を IK で銃に合わせる
    ///  - 構えている時：上半身をひねって狙う方向へ向け、銃を狙う方向へそろえる（脚は移動方向のまま）
    ///  - 撃った時：反動で銃が後ろ・上に跳ね、ばねのように戻る
    /// 骨の向き（軸）に依存しない計算にしているので、モデルを差し替えても動く。
    /// </summary>
    [DefaultExecutionOrder(200)] // Animator・体の傾き（CharacterAnimator）の後に実行する
    public class WeaponHolder : MonoBehaviour
    {
        static readonly int DrawnId = Animator.StringToHash("WeaponDrawn");
        static readonly int AimingId = Animator.StringToHash("Aiming");

        [SerializeField] Animator animator;
        [Tooltip("持ち物（順番に持ち替える）")]
        [SerializeField] WeaponDefinition[] loadout;
        [SerializeField] LineRenderer aimLine;
        [Tooltip("上半身レイヤーの名前（Animator Controller 内）")]
        [SerializeField] string upperBodyLayer = "UpperBody";
        [SerializeField] float upperBodyBlendSpeed = 7f;

        [Header("上半身のひねり")]
        [Tooltip("脚の向きに対して上半身をひねれる最大角度")]
        [SerializeField] float maxTwist = 110f;
        [Tooltip("ひねりを 腰 / 胸 / 胸上部 にどの割合で分けるか")]
        [SerializeField] Vector3 twistWeights = new Vector3(0.25f, 0.35f, 0.4f);
        [SerializeField] float twistSharpness = 18f;

        [Header("反動の戻り")]
        [SerializeField] float recoilReturn = 14f;

        Weapon[] instances;
        int index = -1;
        bool drawn, aiming;
        Vector3 aimDir = Vector3.forward;
        float upperWeight, twist, aimBlend;
        float recoilBack, recoilPitch;
        int layerIndex = -1;
        Transform hand, lowerArm, upperArm, lHand, lLowerArm, lUpperArm;
        Transform thigh, calf, spine, chest, upperChest;

        public WeaponDefinition CurrentWeapon => index >= 0 ? loadout[index] : null;
        public Weapon WeaponInstance => index >= 0 ? instances[index] : null;
        public int CurrentIndex => index;
        public int WeaponCount => loadout != null ? loadout.Length : 0;
        public bool IsDrawn => drawn;
        public bool IsAiming => aiming && drawn;
        public Vector3 AimDirection => aimDir;
        public float UpperBodyWeight => upperWeight;
        public float Twist => twist;
        /// <summary>左手を銃に添える強さ（リロード中は 0 にして手を離す）</summary>
        public float SupportHandWeight { get; set; } = 1f;

        public event Action<WeaponDefinition> WeaponChanged;

        void Awake()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            lowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            upperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            lHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            lLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            lUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            thigh = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            calf = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            upperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            layerIndex = animator.GetLayerIndex(upperBodyLayer);
            if (aimLine) aimLine.enabled = false;

            int n = loadout != null ? loadout.Length : 0;
            instances = new Weapon[n];
            for (int i = 0; i < n; i++)
            {
                var def = loadout[i];
                if (!def || !def.prefab) continue;
                instances[i] = Instantiate(def.prefab, transform);
                instances[i].transform.localScale = Vector3.one * def.visualScale;
            }
            if (n > 0) Select(0);
        }

        /// <summary>持ち物の i 番目に持ち替える。</summary>
        public void Select(int i)
        {
            if (loadout == null || i < 0 || i >= loadout.Length || i == index) return;
            index = i;
            recoilBack = recoilPitch = 0f;
            WeaponChanged?.Invoke(CurrentWeapon);
        }

        public void SelectNext() { if (WeaponCount > 0) Select((index + 1) % WeaponCount); }

        public void SetDrawn(bool value) { if (CurrentWeapon) drawn = value; if (!drawn) aiming = false; }
        public void ToggleDrawn() => SetDrawn(!drawn);

        /// <summary>構えの状態と狙う方向（ワールド・水平）を設定する。毎フレーム呼ぶ。</summary>
        public void SetAim(bool value, Vector3 direction)
        {
            aiming = value && drawn;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f) aimDir = direction.normalized;
        }

        /// <summary>撃った時の反動を加える（WeaponShooter から呼ばれる）。</summary>
        public void AddRecoil(float back, float pitchDeg)
        {
            recoilBack = Mathf.Min(recoilBack + back, 0.15f);
            recoilPitch = Mathf.Min(recoilPitch + pitchDeg, 25f);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            animator.SetBool(DrawnId, drawn);
            animator.SetBool(AimingId, IsAiming);
            upperWeight = Mathf.MoveTowards(upperWeight, drawn ? 1f : 0f, upperBodyBlendSpeed * dt);
            if (layerIndex >= 0) animator.SetLayerWeight(layerIndex, upperWeight);
            aimBlend = Mathf.MoveTowards(aimBlend, IsAiming ? 1f : 0f, 8f * dt);

            float k = 1f - Mathf.Exp(-recoilReturn * dt);
            recoilBack = Mathf.Lerp(recoilBack, 0f, k);
            recoilPitch = Mathf.Lerp(recoilPitch, 0f, k);

            ApplyTwist(dt);
            for (int i = 0; i < instances.Length; i++)
                if (instances[i]) PlaceWeapon(i, i == index);
            UpdateAimLine();
        }

        void ApplyTwist(float dt)
        {
            Vector3 fwd = transform.forward; fwd.y = 0f;
            float target = IsAiming ? Mathf.Clamp(Vector3.SignedAngle(fwd, aimDir, Vector3.up), -maxTwist, maxTwist) : 0f;
            twist = Mathf.Lerp(twist, target, 1f - Mathf.Exp(-twistSharpness * dt));
            if (Mathf.Abs(twist) < 0.01f) return;
            Rotate(spine, twist * twistWeights.x);
            Rotate(chest, twist * twistWeights.y);
            Rotate(upperChest ? upperChest : chest, twist * twistWeights.z);
        }

        static void Rotate(Transform bone, float angle)
        {
            if (bone) bone.rotation = Quaternion.AngleAxis(angle, Vector3.up) * bone.rotation;
        }

        void PlaceWeapon(int i, bool selected)
        {
            var def = loadout[i];
            Transform w = instances[i].transform;
            if (selected && drawn)
            {
                if (def.stance == WeaponStance.Rifle) PlaceRifle(def, instances[i]);
                else PlacePistol(def, w);
            }
            else if (def.holster == HolsterSlot.Back && (upperChest || chest))
            {
                // 背中に斜めにかける（銃口は左上）
                Transform c = upperChest ? upperChest : chest;
                Vector3 dir = (Vector3.up * 0.75f - transform.right * 0.66f).normalized;
                w.SetPositionAndRotation(c.position - transform.forward * 0.2f - Vector3.up * 0.05f,
                    Quaternion.LookRotation(dir, -transform.forward));
            }
            else if (thigh && calf)
            {
                // 右太もも外側、銃口を下に
                Vector3 p = Vector3.Lerp(thigh.position, calf.position, 0.3f) + transform.right * 0.12f;
                w.SetPositionAndRotation(p, Quaternion.LookRotation(Vector3.down, transform.forward));
            }
        }

        Quaternion WithRecoil(Vector3 dir, Vector3 up)
        {
            Vector3 right = Vector3.Cross(up, dir).normalized;
            return Quaternion.AngleAxis(-recoilPitch, right) * Quaternion.LookRotation(dir, up);
        }

        void PlacePistol(WeaponDefinition def, Transform w)
        {
            if (!hand || !lowerArm) return;
            Vector3 forearm = (hand.position - lowerArm.position).normalized;
            Vector3 dir = IsAiming ? aimDir : forearm;           // 構え中は狙いと完全に一致させる
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude < 1e-4f) right = transform.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(dir, right);
            Vector3 g = def.gripOffset;
            Vector3 fRight = Vector3.Cross(Vector3.up, forearm).normalized;
            Vector3 fUp = Vector3.Cross(forearm, fRight);
            Vector3 pos = hand.position + fRight * g.x + fUp * g.y + forearm * g.z - dir * recoilBack;
            w.SetPositionAndRotation(pos, WithRecoil(dir, up));
        }

        void PlaceRifle(WeaponDefinition def, Weapon weapon)
        {
            if (!upperArm) return;
            Transform w = weapon.transform;
            // 狙う向き：構え中は狙いの方向、下げ構えでは体の正面から銃口を下げる
            Vector3 fwd = Vector3.Slerp(FlatForwardOfChest(), aimDir, aimBlend);
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            Vector3 dir = Quaternion.AngleAxis(def.lowReadyPitch * (1f - aimBlend), right) * fwd;
            Vector3 up = Vector3.Cross(dir, right);

            Vector3 s = def.shoulderOffset;
            Vector3 pos = upperArm.position + right * s.x + Vector3.up * s.y + fwd * s.z - dir * recoilBack;
            w.SetPositionAndRotation(pos, WithRecoil(dir, up));

            // 両腕を IK で銃に合わせる（肘は外・下向きに）
            float scale = def.visualScale;
            Vector3 rTarget = w.TransformPoint(def.rightWristOffset / scale);
            Vector3 rHint = upperArm.position + right * 0.5f - Vector3.up * 0.15f - fwd * 0.1f; // 右肘は横へ張る
            TwoBoneIK.Solve(upperArm, lowerArm, hand, rTarget, rHint, upperWeight);

            if (weapon.SupportHand)
            {
                Vector3 lTarget = weapon.SupportHand.position + w.rotation * def.leftWristOffset;
                Vector3 lHint = lUpperArm.position - right * 0.35f - Vector3.up * 0.3f + fwd * 0.15f;
                TwoBoneIK.Solve(lUpperArm, lLowerArm, lHand, lTarget, lHint, upperWeight * SupportHandWeight);
            }
        }

        Vector3 FlatForwardOfChest()
        {
            Vector3 f = Quaternion.AngleAxis(twist, Vector3.up) * transform.forward;
            f.y = 0f;
            return f.normalized;
        }

        void UpdateAimLine()
        {
            if (!aimLine) return;
            var w = WeaponInstance;
            bool show = IsAiming && w && upperWeight > 0.9f;
            aimLine.enabled = show;
            if (!show) return;
            Vector3 start = w.Muzzle.position;
            aimLine.positionCount = 2;
            aimLine.SetPosition(0, start);
            aimLine.SetPosition(1, start + aimDir * CurrentWeapon.aimLineLength);
        }
    }
}
