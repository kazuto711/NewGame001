using Matoi.CameraSystem;
using Matoi.Characters;
using Matoi.Combat;
using UnityEngine;

namespace Matoi.Equipment
{
    /// <summary>
    /// パワーアーマーの重い足音：足が地面に着いた瞬間に、画面揺れ・ドスンという音・土ぼこりを出す。
    /// 足の骨の高さを見て「着地」を判定するので、どの歩行アニメーションでも動く。
    /// </summary>
    public class PowerArmorFootsteps : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField] CharacterMotor motor;
        [Tooltip("足がこの高さ（足元からの m）より下がったら着地とみなす")]
        [SerializeField] float plantHeight = 0.16f;
        [SerializeField] float liftHeight = 0.24f;
        [SerializeField] float minSpeed = 0.5f;
        [SerializeField] float shakeBase = 0.08f;
        [SerializeField] float shakePerSpeed = 0.025f;

        Transform left, right;
        bool leftDown = true, rightDown = true;

        public int Steps { get; private set; }

        void Awake()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!motor) motor = GetComponent<CharacterMotor>();
            left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        }

        void LateUpdate()
        {
            Check(left, ref leftDown);
            Check(right, ref rightDown);
        }

        void Check(Transform foot, ref bool down)
        {
            if (!foot) return;
            float h = foot.position.y - transform.position.y;
            if (down && h > liftHeight) down = false;
            else if (!down && h < plantHeight)
            {
                down = true;
                if (motor.Speed < minSpeed || !motor.IsGrounded) return;
                Steps++;
                CameraShake.Shake(shakeBase + motor.Speed * shakePerSpeed);
                var fx = CombatFx.Instance;
                if (fx)
                {
                    fx.PlaySound(ProceduralSfx.Stomp, 0.7f, 0.1f);
                    fx.Impact(new Vector3(foot.position.x, transform.position.y + 0.05f, foot.position.z), Vector3.up, true);
                }
            }
        }
    }
}
