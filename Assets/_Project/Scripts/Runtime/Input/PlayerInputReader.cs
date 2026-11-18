using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Matoi.InputHandling
{
    /// <summary>
    /// 入力デバイス（ゲームパッド・キーボード・マウス）を読み取り、ゲーム用の値に変換して公開する。
    /// ボタン配置を変えたいときは MatoiControls.inputactions を編集するだけでよい。
    /// </summary>
    public class PlayerInputReader : MonoBehaviour
    {
        [SerializeField] InputActionAsset actions;

        InputAction move, sprint, sprintHold, walk, debugSwap;
        InputAction aim, aimHold, point, drawWeapon, fire;
        InputAction reload, melee, nextWeapon, weapon1, weapon2;

        public event Action ReloadPressed;
        public event Action MeleePressed;
        public event Action NextWeaponPressed;
        /// <summary>数字キーで武器を直接選ぶ（0 始まり）</summary>
        public event Action<int> WeaponSlotPressed;
        public event Action DodgePressed;
        public event Action InteractPressed;
        public event Action SwitchShoulderPressed;
        public event Action ToggleCameraModePressed;

        InputAction look, lookDelta;
        /// <summary>カメラを回す右スティック（-1〜1）。</summary>
        public Vector2 LookStick => look != null ? look.ReadValue<Vector2>() : Vector2.zero;
        /// <summary>このフレームのマウスの移動量（ピクセル）。</summary>
        public Vector2 LookMouseDelta => lookDelta != null ? lookDelta.ReadValue<Vector2>() : Vector2.zero;
        public event Action DebugWavesPressed;

        public Vector2 Move => move != null ? move.ReadValue<Vector2>() : Vector2.zero;
        public bool SprintHeld => sprintHold != null && sprintHold.IsPressed();
        public bool WalkHeld => walk != null && walk.IsPressed();

        /// <summary>右スティックの向き（ゲームパッド）。</summary>
        public Vector2 AimStick => aim != null ? aim.ReadValue<Vector2>() : Vector2.zero;
        /// <summary>LT / 右クリックで構えている。</summary>
        public bool AimHeld => aimHold != null && aimHold.IsPressed();
        /// <summary>マウスカーソルの画面座標。</summary>
        public Vector2 PointerPosition => point != null ? point.ReadValue<Vector2>() : Vector2.zero;
        public bool FireHeld => fire != null && fire.IsPressed();

        /// <summary>最後に操作されたのがゲームパッドか（狙い方・UI 表示の切り替えに使う）</summary>
        public bool UsingGamepad { get; private set; } = true;

        public event Action SprintPressed;
        public event Action DebugSwapPressed;
        public event Action DrawWeaponPressed;
        public event Action FirePressed;

        void Awake()
        {
            if (actions == null) { Debug.LogError("[Matoi] InputActionAsset が設定されていません", this); return; }
            // アセットを複製して使う（複数キャラ・テストで状態が混ざらないように）
            actions = Instantiate(actions);
            var map = actions.FindActionMap("Player", true);
            move = map.FindAction("Move", true);
            sprint = map.FindAction("Sprint", true);
            sprintHold = map.FindAction("SprintHold", true);
            walk = map.FindAction("Walk", true);
            debugSwap = map.FindAction("DebugSwapProfile", true);
            aim = map.FindAction("Aim", true);
            aimHold = map.FindAction("AimHold", true);
            point = map.FindAction("Point", true);
            drawWeapon = map.FindAction("DrawWeapon", true);
            fire = map.FindAction("Fire", true);
            reload = map.FindAction("Reload", true);
            melee = map.FindAction("Melee", true);
            nextWeapon = map.FindAction("NextWeapon", true);
            weapon1 = map.FindAction("Weapon1", true);
            weapon2 = map.FindAction("Weapon2", true);
            reload.performed += c => { TrackDevice(c); ReloadPressed?.Invoke(); };
            melee.performed += c => { TrackDevice(c); MeleePressed?.Invoke(); };
            nextWeapon.performed += c => { TrackDevice(c); NextWeaponPressed?.Invoke(); };
            weapon1.performed += c => { TrackDevice(c); WeaponSlotPressed?.Invoke(0); };
            weapon2.performed += c => { TrackDevice(c); WeaponSlotPressed?.Invoke(1); };
            map.FindAction("Dodge", true).performed += c => { TrackDevice(c); DodgePressed?.Invoke(); };
            map.FindAction("DebugWaves", true).performed += c => DebugWavesPressed?.Invoke();
            map.FindAction("Interact", true).performed += c => { TrackDevice(c); InteractPressed?.Invoke(); };
            look = map.FindAction("Look", true);
            lookDelta = map.FindAction("LookDelta", true);
            look.performed += TrackDevice;
            map.FindAction("SwitchShoulder", true).performed += c => SwitchShoulderPressed?.Invoke();
            map.FindAction("ToggleCameraMode", true).performed += c => ToggleCameraModePressed?.Invoke();
        }

        void OnEnable()
        {
            if (actions == null) return;
            sprint.performed += OnSprint;
            debugSwap.performed += OnDebugSwap;
            drawWeapon.performed += OnDraw;
            fire.performed += OnFire;
            move.performed += TrackDevice;
            aim.performed += TrackDevice;
            aimHold.performed += TrackDevice;
            point.performed += TrackPointer;
            actions.Enable();
        }

        void OnDisable()
        {
            if (actions == null) return;
            sprint.performed -= OnSprint;
            debugSwap.performed -= OnDebugSwap;
            drawWeapon.performed -= OnDraw;
            fire.performed -= OnFire;
            move.performed -= TrackDevice;
            aim.performed -= TrackDevice;
            aimHold.performed -= TrackDevice;
            point.performed -= TrackPointer;
            actions.Disable();
        }

        void OnSprint(InputAction.CallbackContext ctx) { TrackDevice(ctx); SprintPressed?.Invoke(); }
        void OnDebugSwap(InputAction.CallbackContext ctx) => DebugSwapPressed?.Invoke();
        void OnDraw(InputAction.CallbackContext ctx) { TrackDevice(ctx); DrawWeaponPressed?.Invoke(); }
        void OnFire(InputAction.CallbackContext ctx) { TrackDevice(ctx); FirePressed?.Invoke(); }
        void TrackDevice(InputAction.CallbackContext ctx) => UsingGamepad = ctx.control?.device is Gamepad;
        // マウスは少し動かしただけでは切り替えない（机の振動などで誤判定しないため）
        void TrackPointer(InputAction.CallbackContext ctx)
        {
            if (ctx.control?.device is Mouse m && m.delta.ReadValue().sqrMagnitude > 4f) UsingGamepad = false;
        }
    }
}
