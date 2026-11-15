using Matoi.Characters;
using Matoi.InputHandling;
using Matoi.Player;
using UnityEngine;

namespace Matoi.DebugTools
{
    /// <summary>
    /// 開発中だけ使う表示と操作：画面左上に速度などを表示し、Select / F1 で移動プロファイルを切り替える。
    /// 製品版ではこのコンポーネントを外す。
    /// </summary>
    public class PlayerDebugTools : MonoBehaviour
    {
        [SerializeField] CharacterMotor motor;
        [SerializeField] PlayerInputReader input;
        [SerializeField] PlayerController controller;
        [Tooltip("Select / F1 で順番に切り替えるプロファイル（例：人間 → パワーアーマー）")]
        [SerializeField] MovementProfile[] profiles;
        [SerializeField] bool showHud = true;

        int index;
        GUIStyle style;

        void Awake()
        {
            if (!motor) motor = GetComponent<CharacterMotor>();
            if (!input) input = GetComponent<PlayerInputReader>();
            if (!controller) controller = GetComponent<PlayerController>();
            if (profiles != null)
                for (int i = 0; i < profiles.Length; i++)
                    if (profiles[i] == motor.Profile) index = i;
        }

        void OnEnable() { if (input) input.DebugSwapPressed += SwapProfile; }
        void OnDisable() { if (input) input.DebugSwapPressed -= SwapProfile; }

        public void SwapProfile()
        {
            if (profiles == null || profiles.Length == 0) return;
            index = (index + 1) % profiles.Length;
            motor.SetProfile(profiles[index]);
            Matoi.CameraSystem.CameraShake.Shake(0.6f); // 切り替えの手応え（画面揺れの動作確認も兼ねる）
        }

        void OnGUI()
        {
            if (!showHud || motor == null || motor.Profile == null) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 18, normal = { textColor = Color.white } };
            var p = motor.Profile;
            string text =
                $"Profile : {p.name}   (Select / F1 で切替)\n" +
                $"Speed   : {motor.Speed:0.00} m/s  (目標 {motor.DesiredSpeed:0.00})\n" +
                $"State   : {(motor.IsPivoting ? "切り返し" : motor.IsSprinting ? "ダッシュ" : motor.Speed > 0.05f ? "移動" : "停止")}" +
                $"  Turn {motor.TurnRate:0}°/s\n" +
                $"Device  : {(input && input.UsingGamepad ? "Gamepad" : "Keyboard")}";
            var holder = GetComponent<Matoi.Combat.WeaponHolder>();
            var shooter = GetComponent<Matoi.Combat.WeaponShooter>();
            if (holder && holder.CurrentWeapon)
            {
                string reserve = shooter && shooter.ReserveAmmo < 0 ? "∞" : shooter ? shooter.ReserveAmmo.ToString() : "-";
                text += $"\nWeapon  : {holder.CurrentWeapon.displayName}  {(holder.IsDrawn ? "構え" : "収納")}" +
                        $"  弾 {(shooter ? shooter.AmmoInMagazine : 0)}/{reserve}{(shooter && shooter.IsReloading ? "  リロード中" : "")}";
            }
            var hp = GetComponent<Matoi.Combat.Health>();
            if (hp) text += $"\nHP      : {hp.Current:0} / {hp.Max:0}{(hp.IsDead ? "  （倒れた… 3秒後に復活）" : "")}";
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(new Rect(8, 8, 520, 152), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(16, 12, 510, 148), text, style);
        }
    }
}
