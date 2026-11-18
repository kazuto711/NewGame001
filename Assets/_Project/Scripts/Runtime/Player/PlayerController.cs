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
        DodgeRoll dodge;
        Matoi.Equipment.PowerArmorUser armor;
        Matoi.Items.PlayerInteractor interactor;
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
            dodge = GetComponent<DodgeRoll>();
            armor = GetComponent<Matoi.Equipment.PowerArmorUser>();
            interactor = GetComponent<Matoi.Items.PlayerInteractor>();
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
            input.DodgePressed += OnDodgePressed;
            input.InteractPressed += OnInteractPressed;
            input.JumpPressed += OnJumpPressed;
            input.CrouchPressed += OnCrouchPressed;
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
            input.DodgePressed -= OnDodgePressed;
            input.InteractPressed -= OnInteractPressed;
            input.JumpPressed -= OnJumpPressed;
            input.CrouchPressed -= OnCrouchPressed;
        }

        bool Incapacitated => reaction && (reaction.IsStaggered || reaction.Health.IsDead);
        bool IsArmored => armor && armor.IsArmored;

        /// <summary>A / Space：ジャンプ（しゃがみ中は立ち上がって跳ぶ）。</summary>
        void OnJumpPressed()
        {
            if (IsArmored || Incapacitated || IsMeleeAttacking || IsRolling) return;
            motor.TryJump();
        }

        /// <summary>十字キー左 / C：しゃがむ・立つ（押すたびに切り替え）。</summary>
        void OnCrouchPressed()
        {
            if (IsArmored || Incapacitated || IsRolling) return;
            motor.SetCrouch(!motor.CrouchWanted);
        }

        /// <summary>E / ↑：近くの物を調べる。無ければパワーアーマーに乗る・降りる。</summary>
        void OnInteractPressed()
        {
            if (IsMeleeAttacking || IsRolling) return;
            if (interactor && interactor.Press()) return;
            if (!armor) return;
            motor.ForceStand();
            armor.TryInteract();
        }

        void OnDodgePressed()
        {
            if (!dodge || (reaction && (reaction.IsStaggered || reaction.Health.IsDead))) return;
            motor.SetCrouch(false);
            // スティックの方向へ。倒していなければ体の正面へ
            Vector3 dir = ToWorld(input.Move);
            dodge.TryRoll(dir.sqrMagnitude > 0.04f ? dir : transform.forward);
        }

        public bool IsRolling => dodge && dodge.IsRolling;

        void OnSprintPressed() { sprintLatched = true; motor.SetCrouch(false); } // 走り出すと立ち上がる
        void OnDrawPressed()
        {
            // パワーアーマーの近くでは Y でも乗り込める（ゲームパッドで押しやすいように）
            if (armor && !armor.IsArmored && armor.NearestAvailable()) { motor.ForceStand(); armor.TryInteract(); return; }
            if (weapons) weapons.ToggleDrawn();
        }
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
            if (input.SprintHeld && stick.sqrMagnitude > 0.01f) motor.SetCrouch(false);
            if (reaction && reaction.Health.IsDead) motor.SetCrouch(false);

            // 近接攻撃中・ひるみ中・死亡中・採集中は、移動・射撃をしない
            if (IsMeleeAttacking || IsRolling || (interactor && interactor.IsHolding) || (reaction && (reaction.IsStaggered || reaction.Health.IsDead)))
            {
                if (weapons) weapons.SetAim(false, aimDir);
                if (shooter) shooter.SetTrigger(false);
                motor.SetIntent(MoveIntent.None);
                return;
            }

            // ---- 狙い ----
            bool fireHeld = input.FireHeld;
            bool aiming;
            var tp = ThirdPerson;
            if (tp != null)
            {
                // 三人称：右スティック／マウスはカメラを回す。構えは LT / 右クリック（撃つボタンでも構える）
                // 体はカメラの正面を向き、弾は「画面中央の先にある物」へ飛ぶ
                aiming = input.AimHeld || fireHeld;
                if (aiming) aimDir = tp.FlatForward;
                if (weapons && weapons.CurrentWeapon)
                {
                    tp.AimFov = weapons.CurrentWeapon.aimFov;
                    tp.AimSensitivityScale = weapons.CurrentWeapon.aimSensitivityScale;
                }
                if (weapons)
                {
                    weapons.SetAimPoint(aiming ? ScreenCenterAimPoint() : (Vector3?)null);
                    weapons.ShowAimLine = false; // 三人称は照準マークで狙う
                }
            }
            else
            {
                // 見下ろし：右スティック＝狙う方向、マウス＝照準カーソル（従来どおり）
                if (weapons) { weapons.SetAimPoint(null); weapons.ShowAimLine = true; }
                Vector2 aimStick = input.AimStick;
                bool stickAim = aimStick.magnitude > aimStickDeadzone;
                // 撃つボタンだけ押しても、今の向き（直前の狙い）へ構えて撃てる
                aiming = stickAim || input.AimHeld || fireHeld;
                if (fireHeld && !stickAim && !input.AimHeld && weapons && !weapons.IsAiming) aimDir = transform.forward;
                if (stickAim) aimDir = ToWorld(aimStick).normalized;
                else if (aiming && !input.UsingGamepad && TryMouseAim(out var mouseDir)) aimDir = mouseDir;
            }

            if (aiming && weapons && !weapons.IsDrawn) weapons.SetDrawn(true); // 構えたら自動で武器を抜く
            bool armedAim = aiming && weapons && weapons.IsDrawn;
            if (weapons) weapons.SetAim(armedAim, aimDir);
            if (tp != null) tp.AimMode = armedAim;
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

        [Header("三人称の照準")]
        [Tooltip("画面中央の先に何も無い時に狙う距離 (m)")]
        [SerializeField] float maxAimDistance = 80f;
        [Tooltip("この秒数撃たなければ、連射の反動の模様を最初からやり直す")]
        [SerializeField] float recoilPatternReset = 0.35f;
        int burstShots;
        float lastShotTime = -10f;
        readonly RaycastHit[] aimHits = new RaycastHit[16];

        /// <summary>
        /// 画面中央から真っすぐ先にある物の位置（照準点）。
        /// カメラと主人公の間にある物（背後の壁など）は無視するため、主人公の位置から先だけを調べる。
        /// </summary>
        public Vector3 ScreenCenterAimPoint()
        {
            var cam = Camera.main;
            if (!cam) return transform.position + transform.forward * 20f;
            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            float skip = Mathf.Max(0f, Vector3.Dot(transform.position + Vector3.up * 1.2f - ray.origin, ray.direction)) + 0.5f;
            Vector3 start = ray.GetPoint(skip);
            int n = Physics.RaycastNonAlloc(start, ray.direction, aimHits, maxAimDistance, ~0, QueryTriggerInteraction.Ignore);
            float best = maxAimDistance;
            for (int i = 0; i < n; i++)
            {
                if (aimHits[i].collider.CompareTag("Player")) continue;
                if (aimHits[i].distance < best) best = aimHits[i].distance;
            }
            return start + ray.direction * best;
        }

        void OnShotFired(WeaponDefinition def)
        {
            var tp = ThirdPerson;
            if (tp == null || def == null) return;
            burstShots = Time.time - lastShotTime > recoilPatternReset ? 0 : burstShots + 1;
            lastShotTime = Time.time;
            // 連射すると左右にゆっくり揺れながら上がる（覚えて抑えられる模様＋少しのばらつき）
            float stability = shooter ? shooter.StabilityScaleOf(def) : 1f;   // 強化で反動が小さくなる
            float sideways = (Mathf.Sin(burstShots * 0.8f) + Random.Range(-0.35f, 0.35f)) * def.cameraKickSide * stability;
            tp.AddRecoil(def.cameraKickUp * stability, sideways, def.cameraRecoverSpeed, def.cameraRecoverDelay, def.cameraKickMax);
        }

        void Start() { if (shooter) shooter.Fired += OnShotFired; }
        void OnDestroy() { if (shooter) shooter.Fired -= OnShotFired; }

        /// <summary>今が三人称視点なら、そのカメラ。見下ろし視点なら null。</summary>
        static CameraSystem.ThirdPersonCameraController ThirdPerson =>
            CameraSystem.CameraModeSwitcher.Mode == CameraSystem.CameraMode.ThirdPerson
            && CameraSystem.ThirdPersonCameraController.Instance != null
            && CameraSystem.ThirdPersonCameraController.Instance.IsActive
                ? CameraSystem.ThirdPersonCameraController.Instance : null;

        /// <summary>スティックの上 ＝ 画面の奥、になるよう、カメラの水平の向きで回転させる。</summary>
        Vector3 ToWorld(Vector2 stick)
        {
            float yaw;
            var tp = ThirdPerson;
            if (tp != null) yaw = tp.Yaw;   // 三人称：カメラの回転の中心の向き（カメラの追従の遅れに影響されない）
            else
            {
                Transform cam = CameraTransform;
                yaw = cam != null ? cam.eulerAngles.y : 0f;
            }
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
