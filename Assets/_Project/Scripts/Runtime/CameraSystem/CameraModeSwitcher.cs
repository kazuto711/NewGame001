using System;
using Matoi.InputHandling;
using Unity.Cinemachine;
using UnityEngine;

namespace Matoi.CameraSystem
{
    public enum CameraMode
    {
        /// <summary>斜め見下ろし（右スティック＝狙う方向、マウス＝照準カーソル）</summary>
        TopDown,
        /// <summary>三人称・肩越し（右スティック／マウス＝カメラを回す）</summary>
        ThirdPerson,
    }

    /// <summary>
    /// 見下ろし視点と三人称視点を切り替える（F3 / 十字キー下）。比較・検討用に両方を残している。
    /// 操作の意味（右スティックが「狙い」か「カメラ回転」か）も、この設定を見て PlayerController が切り替える。
    /// </summary>
    public class CameraModeSwitcher : MonoBehaviour
    {
        public static CameraMode Mode { get; private set; } = CameraMode.ThirdPerson;
        public static event Action<CameraMode> Changed;

        [SerializeField] CameraMode startMode = CameraMode.ThirdPerson;
        [SerializeField] CinemachineCamera topDownCamera;
        [SerializeField] ThirdPersonCameraController thirdPerson;
        [SerializeField] OcclusionHider occlusionHider;
        [SerializeField] PlayerInputReader input;
        [SerializeField] bool showLabel = true;

        GUIStyle style;

        void Awake() => SetMode(startMode);

        void Start()
        {
            if (!input) { var p = GameObject.FindWithTag("Player"); if (p) input = p.GetComponent<PlayerInputReader>(); }
            if (input) input.ToggleCameraModePressed += Toggle;
        }

        void OnDestroy() { if (input) input.ToggleCameraModePressed -= Toggle; }

        public void Toggle() => SetMode(Mode == CameraMode.TopDown ? CameraMode.ThirdPerson : CameraMode.TopDown);

        public void SetMode(CameraMode mode)
        {
            Mode = mode;
            bool top = mode == CameraMode.TopDown;
            if (topDownCamera) topDownCamera.Priority = top ? 20 : 0;
            if (thirdPerson) thirdPerson.SetActive(!top);
            if (occlusionHider) occlusionHider.enabled = top; // 屋根を透かすのは見下ろしの時だけ
            if (!Application.isBatchMode)
            {
                Cursor.lockState = top ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = top;
            }
            Changed?.Invoke(mode);
        }

        void OnGUI()
        {
            if (!showLabel || !Matoi.Missions.GameFlow.ShowDevInfo) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.UpperRight };
            string text = (Mode == CameraMode.TopDown ? "視点：見下ろし" : "視点：三人称") + "　（F3 / 十字キー↓ で切替）";
            if (Mode == CameraMode.ThirdPerson) text += "\n肩の左右：V / R3";
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.Label(new Rect(Screen.width - 522, 14, 510, 50), text, style);
            GUI.color = Color.white;
            GUI.Label(new Rect(Screen.width - 524, 12, 510, 50), text, style);
        }
    }
}
