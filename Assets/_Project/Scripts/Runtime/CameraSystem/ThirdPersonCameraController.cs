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
        [Tooltip("構え中の回転の速さ（通常に対する割合）。狙いやすくするため遅くする")]
        [SerializeField, Range(0.1f, 1f)] float aimSensitivity = 0.55f;

        [Header("構え用カメラ")]
        [SerializeField] float aimDistance = 1.6f;
        [SerializeField] float aimShoulderX = 0.75f;
        [Tooltip("パワーアーマー（移動プロファイルのカメラ距離 4m）の時に構え用カメラを引く量")]
        [SerializeField] float armorAimExtraDistance = 0.7f;
        [SerializeField] float armorAimExtraShoulder = 0.3f;

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

        // 撃った時の跳ね上がり：プレイヤーの操作（Pitch / Yaw）とは別に持ち、撃ち終わると元の狙いへ戻す
        float recoilPitch, recoilYaw, recoilRecoverSpeed = 8f, recoilResumeAt;
        float fovKick, exploreBaseFov = 55f, aimFovCurrent = 42f;
        float pivotHeight;

        /// <summary>構えた時の画角（武器ごとに PlayerController が設定する）</summary>
        public float AimFov { get; set; } = 42f;
        /// <summary>構え中の回転の速さに掛ける倍率（武器ごと）</summary>
        public float AimSensitivityScale { get; set; } = 1f;
        /// <summary>今の跳ね上がりの量（度）</summary>
        public float RecoilAmount => recoilPitch;
        /// <summary>今の左右の揺れの量（度）</summary>
        public float RecoilYawAmount => recoilYaw;
        /// <summary>跳ね上がりを含めた、実際のカメラの上下の角度</summary>
        public float ViewPitch => Pitch - recoilPitch;

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
            if (exploreCamera) exploreBaseFov = exploreCamera.Lens.FieldOfView;
            if (aimCamera) AimFov = aimFovCurrent = aimCamera.Lens.FieldOfView;
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
            // しゃがんだ時に視点がカクッと動かないよう、高さはなめらかに追う
            float wanted = cc ? cc.height : 1.8f;
            pivotHeight = pivotHeight <= 0f ? wanted : Mathf.Lerp(pivotHeight, wanted, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
            transform.SetPositionAndRotation(target.position + Vector3.up * pivotHeight * pivotHeightRatio,
                Quaternion.Euler(Mathf.Max(Pitch - recoilPitch, pitchLimits.x), Yaw + recoilYaw, 0f));
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
                float sens = AimMode ? aimSensitivity * AimSensitivityScale : 1f;
                Yaw += (stick.x * stickSpeed.x * dt + mouse.x * mouseSensitivity) * sens;
                float pitchBefore = Pitch;
                Pitch -= (stick.y * stickSpeed.y * dt + mouse.y * mouseSensitivity) * ySign * sens;
                Pitch = Mathf.Clamp(Pitch, pitchLimits.x, pitchLimits.y);
                // 跳ね上がりを自分で押さえた分は、後で戻す量から差し引く（戻りすぎて照準が下がらないように）
                float pulledDown = Pitch - pitchBefore;
                if (pulledDown > 0f && recoilPitch > 0f)
                {
                    float absorbed = Mathf.Min(pulledDown, recoilPitch);
                    recoilPitch -= absorbed;
                    Pitch -= absorbed;
                }
                HandleCursor();
            }
            UpdateRecoilAndLens();
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
            // 構え用カメラ：パワーアーマーの時は体が大きいので少し引いて、肩も外へ
            if (aimFollow)
            {
                float armorT = motor && motor.Profile ? Mathf.Clamp01(motor.Profile.cameraDistanceOffset / 4f) : 0f;
                aimFollow.CameraDistance = aimDistance + armorAimExtraDistance * armorT;
                var so = aimFollow.ShoulderOffset;
                so.x = aimShoulderX + armorAimExtraShoulder * armorT;
                aimFollow.ShoulderOffset = so;
            }
            if (aimCamera) aimCamera.Priority = active && AimMode ? 25 : 0;
        }

        /// <summary>
        /// 撃った時の反動でカメラを跳ね上げる（上が＋、度）。撃つのをやめると元の狙いへ戻る。
        /// </summary>
        /// <param name="recoverSpeed">戻る速さ（度/秒）</param>
        /// <param name="recoverDelay">最後に撃ってから戻り始めるまで（秒）</param>
        /// <param name="maxUp">跳ね上がりの上限（度）</param>
        public void AddRecoil(float up, float sideways, float recoverSpeed, float recoverDelay, float maxUp)
        {
            if (!active) return;
            recoilPitch = Mathf.Min(recoilPitch + up, maxUp);
            recoilYaw = Mathf.Clamp(recoilYaw + sideways, -maxUp * 0.5f, maxUp * 0.5f);
            recoilRecoverSpeed = recoverSpeed;
            recoilResumeAt = Time.unscaledTime + recoverDelay;
        }

        /// <summary>強い攻撃が当たった瞬間に、画面を一瞬だけ寄せる（度）。</summary>
        public void AddFovKick(float degrees) => fovKick = Mathf.Max(fovKick, degrees);

        void UpdateRecoilAndLens()
        {
            float dt = Time.unscaledDeltaTime;
            if (Time.unscaledTime >= recoilResumeAt)
            {
                float step = recoilRecoverSpeed * dt;
                float yawStep = recoilPitch > 0.01f ? step * Mathf.Abs(recoilYaw) / recoilPitch : step;
                recoilPitch = Mathf.MoveTowards(recoilPitch, 0f, step);
                recoilYaw = Mathf.MoveTowards(recoilYaw, 0f, yawStep);
            }

            fovKick = Mathf.Lerp(fovKick, 0f, 1f - Mathf.Exp(-9f * dt));
            aimFovCurrent = Mathf.Lerp(aimFovCurrent, AimFov, 1f - Mathf.Exp(-10f * dt));
            if (exploreCamera)
            {
                var lens = exploreCamera.Lens;
                lens.FieldOfView = exploreBaseFov - fovKick;
                exploreCamera.Lens = lens;
            }
            if (aimCamera)
            {
                var lens = aimCamera.Lens;
                lens.FieldOfView = aimFovCurrent - fovKick;
                aimCamera.Lens = lens;
            }
        }

        /// <summary>マウス操作の時はカーソルを画面に固定。メニューを開いている間は自由に動かせる。</summary>
        void HandleCursor()
        {
            if (Application.isBatchMode || Matoi.UI.MenuState.IsOpen) return;
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }
    }
}
