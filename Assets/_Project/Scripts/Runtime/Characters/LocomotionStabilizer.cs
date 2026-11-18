using UnityEngine;

namespace Matoi.Characters
{
    /// <summary>
    /// 歩き・走りのアニメーションで、腰の左右ぶれと上半身のゆらゆらを抑える。
    /// アニメーションを再生した後の姿勢を少しだけ補正する。足は地面に着いた位置のまま保つ。
    /// 骨の軸に依存しない計算なので、モデルを差し替えても動く。
    /// </summary>
    [DefaultExecutionOrder(150)] // Animator・体の傾きの後、武器の配置（WeaponHolder）の前に実行する
    public class LocomotionStabilizer : MonoBehaviour
    {
        [Tooltip("腰の左右ぶれを打ち消す割合")]
        [SerializeField, Range(0f, 1f)] float hipSway = 0.65f;
        [Tooltip("背骨の左右の傾きを打ち消す割合")]
        [SerializeField, Range(0f, 1f)] float torsoRoll = 0.75f;
        [Tooltip("肩のひねりを打ち消す割合")]
        [SerializeField, Range(0f, 1f)] float torsoYaw = 0.65f;
        [Tooltip("肩の左右の高さの差を打ち消す割合")]
        [SerializeField, Range(0f, 1f)] float shoulderTilt = 0.75f;
        [Tooltip("全力疾走では腕振りの勢いを残すため、補正をこの割合まで弱める")]
        [SerializeField, Range(0f, 1f)] float sprintStrength = 0.5f;
        [SerializeField] float blendSpeed = 6f;

        CharacterMotor motor;
        CharacterAnimator characterAnimator;
        Animator animator;
        Transform hips, spine, chest, upperChest, neck;
        Transform lUpperArm, rUpperArm;
        Transform lThigh, lCalf, lFoot, rThigh, rCalf, rFoot;
        float weight;

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            characterAnimator = GetComponent<CharacterAnimator>();
            animator = GetComponentInChildren<Animator>();
            if (!animator || !animator.isHuman) { enabled = false; return; }
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            upperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            lUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            lThigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            lCalf = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            lFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            rThigh = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            rCalf = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            rFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        }

        bool ShouldStabilize => motor && motor.IsGrounded && !motor.IsAirborne && !motor.IsDashing && !motor.IsCrouching
                                && motor.Speed > 0.05f && !(characterAnimator && characterAnimator.ActionOwnsArms);

        void LateUpdate()
        {
            weight = Mathf.MoveTowards(weight, ShouldStabilize ? 1f : 0f, blendSpeed * Time.deltaTime);
            if (weight <= 0f || !hips || !spine || !neck || !lUpperArm || !rUpperArm) return;

            float sprint = motor.Profile ? Mathf.InverseLerp(motor.Profile.jogSpeed, motor.Profile.sprintSpeed, motor.Speed) : 0f;
            float w = weight * Mathf.Lerp(1f, sprintStrength, sprint);
            Transform root = animator.transform;

            StabilizeHips(root, w);
            StabilizeTorso(root, w);
        }

        void StabilizeHips(Transform root, float w)
        {
            Vector3 lPos = lFoot.position, rPos = rFoot.position;
            Quaternion lRot = lFoot.rotation, rRot = rFoot.rotation;

            float side = Vector3.Dot(hips.position - root.position, root.right);
            hips.position -= root.right * (side * hipSway * w);

            // 腰を動かした分、足が地面の上を滑らないように脚を合わせ直す
            Combat.TwoBoneIK.Solve(lThigh, lCalf, lFoot, lPos, lCalf.position + root.forward * 0.4f);
            Combat.TwoBoneIK.Solve(rThigh, rCalf, rFoot, rPos, rCalf.position + root.forward * 0.4f);
            lFoot.rotation = lRot;
            rFoot.rotation = rRot;
        }

        void StabilizeTorso(Transform root, float w)
        {
            Transform top = upperChest ? upperChest : chest ? chest : spine;

            // 背骨全体の左右の傾き
            Vector3 up = Vector3.ProjectOnPlane(neck.position - hips.position, root.forward);
            float roll = Vector3.SignedAngle(root.up, up, root.forward);
            Spread(root.forward, -roll * torsoRoll * w, top);

            // 肩のひねり
            Vector3 shoulders = Vector3.ProjectOnPlane(rUpperArm.position - lUpperArm.position, root.up);
            float yaw = Vector3.SignedAngle(root.right, shoulders, root.up);
            Spread(root.up, -yaw * torsoYaw * w, top);

            // 肩の左右の高さの差（胸の上部だけで直す）
            Vector3 level = Vector3.ProjectOnPlane(rUpperArm.position - lUpperArm.position, root.forward);
            float tilt = Vector3.SignedAngle(root.right, level, root.forward);
            top.rotation = Quaternion.AngleAxis(-tilt * shoulderTilt * w, root.forward) * top.rotation;
        }

        void Spread(Vector3 axis, float angle, Transform top)
        {
            spine.rotation = Quaternion.AngleAxis(angle * 0.4f, axis) * spine.rotation;
            if (chest && chest != top) chest.rotation = Quaternion.AngleAxis(angle * 0.3f, axis) * chest.rotation;
            top.rotation = Quaternion.AngleAxis(angle * (chest && chest != top ? 0.3f : 0.6f), axis) * top.rotation;
        }
    }
}
