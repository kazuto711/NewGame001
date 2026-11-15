using Matoi.Characters;
using Matoi.Combat;
using Matoi.InputHandling;
using UnityEngine;

namespace Matoi.Player
{
    /// <summary>
    /// プレイヤーの「頭脳」。入力を読み、画面の向きに合わせた移動・狙いの意図に変換して Motor / WeaponHolder に渡す。
    /// 敵は同じ Motor・WeaponHolder を使い、この部分だけ AI に置き換わる。
    /// </summary>
    [RequireComponent(typeof(CharacterMotor))]
    [DefaultExecutionOrder(-50)] // Motor より先に意図を渡す（1フレーム遅れを防ぐ）
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] PlayerInputReader input;
        [SerializeField] WeaponHolder weapons;
        [SerializeField] WeaponShooter shooter;
        [SerializeField] MeleeAttack melee;
        [Tooltip("入力方向の基準にするカメラ。空ならメインカメラ")]
        [SerializeField] Transform cameraTransform;

        [Header("ダッシュ（ゲームパッド：L3 を押すと開始、止まると解除）")]
        [Tooltip("スティックの倒し具合がこれ未満になったらダッシュを解除")]
        [SerializeField, Range(0f, 1f)] float sprintReleaseThreshold = 0.35f;

        [Header("狙い（ゲームパッド：右スティックを倒すと構える / マウス：右クリックで構える）")]
        [SerializeField, Range(0f, 1f)] float aimStickDeadzone = 0.3f;
        [Tooltip("移動方向と狙いがこの角度以上離れたら、後ずさり（狙う方向を向いたまま後退）にする")]
        [SerializeField] float backpedalEnterAngle = 115f;
        [SerializeField] float backpedalExitAngle = 95f;
        [Tooltip("マウスで狙う時の、狙いの高さ（足元からの m）")]
        [SerializeField] float mouseAimHeight = 1.2f;

        CharacterMotor motor;
        HitReaction reaction;
        bool sprintLatched, backpedal;
        Vector3 aimDir;

        public bool SprintLatched => sprintLatched;
        public bool IsBackpedaling => backpedal;
        public Vector3 AimDirection => aimDir;

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (weapons == null) weapons = GetComponent<WeaponHolder>();
            if (shooter == null) shooter = GetComponent<WeaponShooter>();
            if (melee == null) melee = GetComponent<MeleeAttack>();
            reaction = GetComponent<HitReaction>();
            aimDir = transform.forward;
        }

        void OnEnable()
        {
            if (!input) return;
            input.SprintPressed += OnSprintPressed;
            input.DrawWeaponPressed += OnDrawPressed;
            input.ReloadPressed += OnReloadPressed;
            input.MeleePressed += OnMeleePressed;
            input.NextWeaponPressed += OnNextWeapon;
            input.WeaponSlotPressed += OnWeaponSlot;
        }

        void OnDisable()
        {
            if (!input) return;
            input.SprintPressed -= OnSprintPressed;
            input.DrawWeaponPressed -= OnDrawPressed;
            input.ReloadPressed -= OnReloadPressed;
            input.MeleePressed -= OnMeleePressed;
            input.NextWeaponPressed -= OnNextWeapon;
            input.WeaponSlotPressed -= OnWeaponSlot;
        }

        void OnSprintPressed() => sprintLatched = true;
        void OnDrawPressed() { if (weapons) weapons.ToggleDrawn(); }
        void OnReloadPressed() { if (shooter && weapons && weapons.IsDrawn) shooter.Reload(); }
        void OnNextWeapon() { if (weapons) { weapons.SelectNext(); weapons.SetDrawn(true); } }
        void OnWeaponSlot(int i) { if (weapons) { weapons.Select(i); weapons.SetDrawn(true); } }

        void OnMeleePressed()
        {
            if (!melee || (reaction && (reaction.IsStaggered || reaction.Health.IsDead))) return;
            // 動いている方向 → 狙っている方向 → 正面 の順で攻撃方向を決める
            Vector3 dir = ToWorld(input.Move);
            if (dir.sqrMagnitude < 0.04f) dir = weapons && weapons.IsAiming ? aimDir : transform.forward;
            melee.TryAttack(dir);
        }

        public bool IsMeleeAttacking => melee && melee.IsAttacking;

        void Update()
        {
            if (input == null) return;
            Vector2 stick = Vector2.ClampMagnitude(input.Move, 1f);
            Vector3 moveDir = ToWorld(stick);

            // 近接攻撃中・ひるみ中・死亡中は、移動・射撃をしない
            if (IsMeleeAttacking || (reaction && (reaction.IsStaggered || reaction.Health.IsDead)))
            {
                if (weapons) weapons.SetAim(false, aimDir);
                if (shooter) shooter.SetTrigger(false);
                motor.SetIntent(MoveIntent.None);
                return;
            }

            // ---- 狙い ----
            Vector2 aimStick = input.AimStick;
            bool stickAim = aimStick.magnitude > aimStickDeadzone;
            bool fireHeld = input.FireHeld;
            // 撃つボタンだけ押しても、今の向き（直前の狙い）へ構えて撃てる
            bool aiming = stickAim || input.AimHeld || fireHeld;
            if (fireHeld && !stickAim && !input.AimHeld && weapons && !weapons.IsAiming) aimDir = transform.forward;
            if (stickAim) aimDir = ToWorld(aimStick).normalized;
            else if (aiming && !input.UsingGamepad && TryMouseAim(out var mouseDir)) aimDir = mouseDir;

            if (aiming && weapons && !weapons.IsDrawn) weapons.SetDrawn(true); // 構えたら自動で武器を抜く
            bool armedAim = aiming && weapons && weapons.IsDrawn;
            if (weapons) weapons.SetAim(armedAim, aimDir);
            if (shooter) shooter.SetTrigger(armedAim && fireHeld);

            // ---- 移動 ----
            if (stick.magnitude < sprintReleaseThreshold || armedAim) sprintLatched = false;

            var intent = new MoveIntent
            {
                direction = moveDir,
                sprint = !armedAim && (sprintLatched || input.SprintHeld),
                walk = input.WalkHeld,
            };

            if (armedAim)
            {
                // 脚は移動方向を向き、上半身のひねりで狙う。反対方向へ動く時は後ずさり。
                float mult = motor.Profile ? motor.Profile.aimMoveSpeedFactor : 0.6f;
                if (weapons.CurrentWeapon) mult *= weapons.CurrentWeapon.aimMoveSpeedMultiplier;
                intent.speedMultiplier = mult;
                if (stick.magnitude < 0.1f)
                {
                    backpedal = false;
                    intent.facing = aimDir;
                }
                else
                {
                    float angle = Vector3.Angle(moveDir, aimDir);
                    if (!backpedal && angle > backpedalEnterAngle) backpedal = true;
                    else if (backpedal && angle < backpedalExitAngle) backpedal = false;
                    intent.facing = backpedal ? -moveDir.normalized : moveDir.normalized;
                }
            }
            else backpedal = false;

            motor.SetIntent(intent);
        }

        /// <summary>スティックの上 ＝ 画面の奥、になるよう、カメラの水平の向きで回転させる。</summary>
        Vector3 ToWorld(Vector2 stick)
        {
            Transform cam = CameraTransform;
            float yaw = cam != null ? cam.eulerAngles.y : 0f;
            return Quaternion.Euler(0f, yaw, 0f) * new Vector3(stick.x, 0f, stick.y);
        }

        Transform CameraTransform
        {
            get
            {
                if (cameraTransform) return cameraTransform;
                return Camera.main ? Camera.main.transform : null;
            }
        }

        bool TryMouseAim(out Vector3 dir)
        {
            dir = default;
            var cam = Camera.main;
            if (!cam) return false;
            var ray = cam.ScreenPointToRay(input.PointerPosition);
            var plane = new Plane(Vector3.up, transform.position + Vector3.up * mouseAimHeight);
            if (!plane.Raycast(ray, out float t)) return false;
            dir = ray.GetPoint(t) - (transform.position + Vector3.up * mouseAimHeight);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.04f) return false;
            dir.Normalize();
            return true;
        }
    }
}
