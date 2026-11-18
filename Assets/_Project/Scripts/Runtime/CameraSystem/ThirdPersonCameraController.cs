using Matoi.Characters;
using Matoi.InputHandling;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Matoi.CameraSystem
{
    /// <summary>
    /// 三人称カメラの「回転の中心（CameraRoot）」を動かす。
    ///  - 主人公の頭の後ろあたりに付いていき、右スティック／マウスで左右・上下に回す
    ///  - 探索用カメラ（背後）と構え用カメラ（肩越し）を切り替える
    ///  - 肩の左右切り替え、ダッシュ・パワーアーマー時に少し引く
    /// 実際のカメラ位置・壁よけは Cinemachine の ThirdPersonFollow が行う。
    /// </summary>
    [DefaultExecutionOrder(-100)] // Cinemachine より先に回転を決める
    public class ThirdPersonCameraController : MonoBehaviour
    {
        public static ThirdPersonCameraController Instance { get; private set; }

        [SerializeField] Transform target;
        [SerializeField] PlayerInputReader input;
        [SerializeField] CharacterMotor motor;
        [SerializeField] CinemachineCamera exploreCamera;
        [SerializeField] CinemachineCamera aimCamera;
        [SerializeField] CinemachineThirdPersonFollow exploreFollow;
        [SerializeField] CinemachineThirdPersonFollow aimFollow;

        [Header("回転")]
        [Tooltip("右スティックを倒しきった時の回転速度（度/秒）左右・上下")]
        [SerializeField] Vector2 stickSpeed = new Vector2(200f, 130f);
        [Tooltip("マウス 1 ピクセルあたりの回転（度）")]
        [SerializeField] float mouseSensitivity = 0.12f;
        [SerializeField] bool invertY = false;
        [Tooltip("上下の角度の範囲（下向きが＋）")]
        [SerializeField] Vector2 pitchLimits = new Vector2(-35f, 65f);
        [SerializeField] float startPitch = 12f;

        [Header("位置")]
        [Tooltip("回転の中心の高さ（キャラの身長に対する割合）")]
        [SerializeField] float pivotHeightRatio = 0.86f;
        [Tooltip("肩の左右を切り替える速さ（大きいほど速い）")]
        [SerializeField] float shoulderSwitchSpeed = 8f;

        [Header("距離")]
        [SerializeField] float exploreDistance = 3.8f;
        [SerializeField] float sprintExtraDistance = 0.6f;
        [Tooltip("移動プロファイルのカメラ距離（見下ろし用）に掛けて三人称で使う割合")]
        [SerializeField] float profileDistanceScale = 0.25f;

        CharacterController cc;
        float side = 1f;
        bool active;

        public float Yaw { get; set; }
        public float Pitch { get; set; }
        public bool RightShoulder { get; private set; } = true;
        /// <summary>構え中か（PlayerController が毎フレーム設定する）。true だと肩越しカメラになる</summary>
        public bool AimMode { get; set; }
        public bool IsActive => active;
        /// <summary>カメラの水平の正面方向（移動の基準・仮の照準に使う）</summary>
        public Vector3 FlatForward => Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward;
        public float CurrentSide => side;
        public float CurrentExploreDistance => exploreFollow ? exploreFollow.CameraDistance : 0f;

        void Awake()
        {
            Instance = this;
            if (target) cc = target.GetComponent<CharacterController>();
            if (target) Yaw = target.eulerAngles.y;
            Pitch = startPitch;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void OnEnable() { if (input) input.SwitchShoulderPressed += ToggleShoulder; }
        void OnDisable() { if (input) input.SwitchShoulderPressed -= ToggleShoulder; }

        public void ToggleShoulder() { if (active) RightShoulder = !RightShoulder; }

        /// <summary>三人称カメラを使う／使わない。使い始めは主人公の真後ろから。</summary>
        public void SetActive(bool on)
        {
            active = on;
            if (on && target)
            {
                Yaw = target.eulerAngles.y;
                Pitch = startPitch;
                SnapToTarget();
            }
            if (exploreCamera) exploreCamera.Priority = on ? 15 : 0;
            if (aimCamera) aimCamera.Priority = 0;
        }

        void SnapToTarget()
        {
            if (!target) return;
            float h = cc ? cc.height : 1.8f;
            transform.SetPositionAndRotation(target.position + Vector3.up * h * pivotHeightRatio,
                Quaternion.Euler(Pitch, Yaw, 0f));
        }

        void LateUpdate()
        {
            if (!target) return;
            if (active && input)
            {
                float dt = Time.unscaledDeltaTime; // ヒットストップ中もカメラは回せる
                Vector2 stick = input.LookStick;
                Vector2 mouse = Cursor.lockState == CursorLockMode.Locked ? input.LookMouseDelta : Vector2.zero;
                float ySign = invertY ? -1f : 1f;
                Yaw += stick.x * stickSpeed.x * dt + mouse.x * mouseSensitivity;
                Pitch -= (stick.y * stickSpeed.y * dt + mouse.y * mouseSensitivity) * ySign;
                Pitch = Mathf.Clamp(Pitch, pitchLimits.x, pitchLimits.y);
                HandleCursor();
            }
            SnapToTarget();

            // 肩の左右（0=左, 1=右）をなめらかに
            side = Mathf.MoveTowards(side, RightShoulder ? 1f : 0f, shoulderSwitchSpeed * Time.deltaTime);
            if (exploreFollow) exploreFollow.CameraSide = side;
            if (aimFollow) aimFollow.CameraSide = side;

            // 距離：ダッシュ・パワーアーマーで少し引く
            if (exploreFollow)
            {
                float want = exploreDistance;
                if (motor && motor.Profile)
                {
                    var p = motor.Profile;
                    want += p.cameraDistanceOffset * profileDistanceScale;
                    want += sprintExtraDistance * Mathf.InverseLerp(p.jogSpeed, p.sprintSpeed, motor.Speed);
                }
                exploreFollow.CameraDistance = Mathf.Lerp(exploreFollow.CameraDistance, want, 1f - Mathf.Exp(-4f * Time.deltaTime));
            }
            if (aimCamera) aimCamera.Priority = active && AimMode ? 25 : 0;
        }

        /// <summary>マウス操作の時はカーソルを画面に固定。Esc で解除、クリックで再び固定。</summary>
        void HandleCursor()
        {
            if (Application.isBatchMode) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            else if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }
    }
}
