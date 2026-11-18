using UnityEngine;

namespace Matoi.Characters
{
    /// <summary>
    /// キャラクターの「動きの性格」をまとめたデータ。
    /// 通常の人間とパワーアーマーは、このファイルを差し替えるだけで重さが変わる。
    /// Project ウィンドウで右クリック → Create → Matoi → Movement Profile で作成できる。
    /// </summary>
    [CreateAssetMenu(menuName = "Matoi/Movement Profile", fileName = "MovementProfile")]
    public class MovementProfile : ScriptableObject
    {
        [Header("速度 (m/秒)")]
        [Tooltip("スティックを軽く倒したとき / 歩きボタン")]
        public float walkSpeed = 1.7f;
        [Tooltip("スティックを最後まで倒したとき")]
        public float jogSpeed = 4.2f;
        [Tooltip("ダッシュ中")]
        public float sprintSpeed = 6.6f;
        [Tooltip("スティックの倒し具合がこれ以下なら歩き、以上なら歩き→小走りへ補間")]
        [Range(0.1f, 0.9f)] public float walkInputThreshold = 0.5f;

        [Header("加速・減速 (m/秒²)")]
        public float acceleration = 16f;
        public float sprintAcceleration = 9f;
        public float deceleration = 22f;

        [Header("旋回 (度/秒)")]
        [Tooltip("止まっている時の旋回速度")]
        public float turnSpeedIdle = 1080f;
        [Tooltip("小走り中の旋回速度")]
        public float turnSpeedMoving = 620f;
        [Tooltip("ダッシュ中の旋回速度（遅いほど大回りになる）")]
        public float turnSpeedSprint = 320f;
        [Tooltip("向きと入力方向が大きくずれている間、速度をこの割合まで落とす")]
        [Range(0f, 1f)] public float sharpTurnSpeedFactor = 0.45f;

        [Header("構え中")]
        [Tooltip("構えて移動する時の速度倍率")]
        [Range(0.2f, 1f)] public float aimMoveSpeedFactor = 0.6f;
        [Tooltip("構え中に体の向きを変える速さ（度/秒）")]
        public float aimTurnSpeed = 720f;

        [Header("切り返し（急な反転）")]
        [Tooltip("入力がこの角度以上反対を向いたら、一度踏ん張ってから向きを変える")]
        [Range(90f, 180f)] public float pivotAngle = 135f;
        [Tooltip("この速度以上で走っている時だけ切り返しになる")]
        public float pivotMinSpeed = 2.5f;
        [Tooltip("切り返しの踏ん張り時の減速")]
        public float pivotDeceleration = 34f;

        [Header("入力")]
        [Tooltip("スティック入力のなまし時間（秒）。大きいほど重く、鈍くなる")]
        [Range(0f, 0.3f)] public float inputSmoothing = 0.05f;

        [Header("カメラ")]
        [Tooltip("このプロファイルの時にカメラを追加で引く距離 (m)。大きな機体ほど広く見せる")]
        public float cameraDistanceOffset = 0f;

        [Header("ジャンプ")]
        public bool canJump = true;
        [Tooltip("ジャンプの高さ (m)")]
        public float jumpHeight = 1.1f;
        [Tooltip("空中で向きや速度を変えられる割合（0=変えられない, 1=地上と同じ）")]
        [Range(0f, 1f)] public float airControl = 0.35f;

        [Header("しゃがみ")]
        public bool canCrouch = true;
        [Tooltip("しゃがみ歩きの速さ (m/秒)")]
        public float crouchSpeed = 1.7f;
        [Tooltip("しゃがんだ時の当たり判定の高さ（立っている時に対する割合）")]
        [Range(0.4f, 0.9f)] public float crouchHeightRatio = 0.62f;

        [Header("重力")]
        public float gravity = -25f;
        public float maxFallSpeed = -40f;

        public float GetTurnSpeed(float currentSpeed)
        {
            if (currentSpeed <= jogSpeed)
                return Mathf.Lerp(turnSpeedIdle, turnSpeedMoving, Mathf.InverseLerp(0f, jogSpeed, currentSpeed));
            return Mathf.Lerp(turnSpeedMoving, turnSpeedSprint, Mathf.InverseLerp(jogSpeed, sprintSpeed, currentSpeed));
        }
    }
}
