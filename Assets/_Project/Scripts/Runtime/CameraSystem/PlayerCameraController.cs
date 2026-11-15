using Matoi.Characters;
using Unity.Cinemachine;
using UnityEngine;

namespace Matoi.CameraSystem
{
    /// <summary>
    /// 見下ろしカメラの「演出」を担当：走る速さに応じて少し引く／移動プロファイル（パワーアーマー等）ごとの距離。
    /// 追従・先読みそのものは Cinemachine（PositionComposer）が行う。
    /// </summary>
    [RequireComponent(typeof(CinemachineCamera))]
    public class PlayerCameraController : MonoBehaviour
    {
        [SerializeField] CinemachinePositionComposer composer;
        [SerializeField] CharacterMotor motor;

        [Tooltip("基本のカメラ距離 (m)")]
        [SerializeField] float baseDistance = 24f;
        [Tooltip("ダッシュ時に追加で引く距離 (m)")]
        [SerializeField] float sprintExtraDistance = 3f;
        [Tooltip("距離の変化のなめらかさ（大きいほど速く追従）")]
        [SerializeField] float zoomSharpness = 2.5f;

        public float BaseDistance { get => baseDistance; set => baseDistance = value; }
        public float CurrentDistance => composer ? composer.CameraDistance : 0f;

        void Awake()
        {
            if (!composer) composer = GetComponent<CinemachinePositionComposer>();
        }

        public void SetTarget(CharacterMotor m) => motor = m;

        void LateUpdate()
        {
            if (!composer) return;
            float target = baseDistance;
            if (motor && motor.Profile)
            {
                var p = motor.Profile;
                target += p.cameraDistanceOffset;
                float sprintT = Mathf.InverseLerp(p.jogSpeed, p.sprintSpeed, motor.Speed);
                target += sprintExtraDistance * sprintT;
            }
            float k = 1f - Mathf.Exp(-zoomSharpness * Time.deltaTime);
            composer.CameraDistance = Mathf.Lerp(composer.CameraDistance, target, k);
        }
    }
}
