using UnityEngine;

namespace Matoi.Combat
{
    public enum WeaponStance
    {
        Pistol,   // 両手で前に構える拳銃（アニメーションの姿勢をそのまま使う）
        Rifle,    // 肩付けして両手で持つ長物（両腕を IK で銃に合わせる）
    }

    public enum HolsterSlot
    {
        RightThigh,
        Back,
    }

    public enum ShotSound
    {
        Pistol,
        Rifle,
    }

    /// <summary>
    /// 武器の性能と持ち方をまとめたデータ。武器を増やす時はこのファイルを複製して値を変える。
    /// Project ウィンドウで右クリック → Create → Matoi → Weapon Definition。
    /// </summary>
    [CreateAssetMenu(menuName = "Matoi/Weapon Definition", fileName = "Weapon")]
    public class WeaponDefinition : ScriptableObject
    {
        [Header("基本")]
        public string displayName = "拳銃";
        [Tooltip("見た目のプレハブ（Weapon コンポーネント付き）")]
        public Weapon prefab;
        public WeaponStance stance = WeaponStance.Pistol;
        public HolsterSlot holster = HolsterSlot.RightThigh;
        [Tooltip("見た目の拡大率。見下ろし視点で武器が見えるよう、実寸より少し大きくする")]
        public float visualScale = 1.4f;

        [Header("持ち方：拳銃（手首からの位置。x=右, y=上, z=前腕の向き）")]
        public Vector3 gripOffset = new Vector3(0f, -0.01f, 0.075f);

        [Header("持ち方：ライフル（右肩からの位置。x=右, y=上, z=狙う向き）")]
        public Vector3 shoulderOffset = new Vector3(-0.02f, -0.08f, 0.30f);
        [Tooltip("構えていない時（下げ構え）に銃口を下げる角度")]
        public float lowReadyPitch = 40f;
        [Tooltip("右手首の位置（銃のローカル座標。グリップからのずれ）")]
        public Vector3 rightWristOffset = new Vector3(0.025f, -0.035f, -0.05f);
        [Tooltip("左手首の位置（銃のローカル座標。SupportHand からのずれ）")]
        public Vector3 leftWristOffset = new Vector3(-0.03f, -0.05f, -0.04f);

        [Header("構え")]
        [Tooltip("構え中の移動速度倍率（移動プロファイルの値に掛ける）")]
        [Range(0.2f, 1f)] public float aimMoveSpeedMultiplier = 1f;
        [Tooltip("照準線の長さ (m)")]
        public float aimLineLength = 9f;

        [Header("射撃")]
        public float damage = 20f;
        [Tooltip("1秒あたりの発射数")]
        public float fireRate = 5f;
        [Tooltip("ボタンを押し続けると連射するか（フルオート）")]
        public bool automatic = false;
        public float range = 30f;
        [Tooltip("弾のばらつき（度）。止まっている時")]
        public float spread = 1.0f;
        [Tooltip("移動中に追加されるばらつき（度）")]
        public float moveSpread = 3f;
        [Tooltip("当たった相手を押す強さ (m/s)")]
        public float knockback = 1.2f;

        [Header("弾数")]
        public int magazineSize = 12;
        [Tooltip("予備の弾（-1 なら無限）")]
        public int reserveAmmo = -1;
        public float reloadTime = 1.2f;

        [Header("反動・演出")]
        [Tooltip("1発ごとに銃が後ろへ下がる量 (m)")]
        public float recoilKick = 0.05f;
        [Tooltip("1発ごとに銃口が跳ね上がる角度（度）")]
        public float recoilPitch = 8f;
        [Tooltip("反動による画面揺れの強さ")]
        public float recoilShake = 0.12f;
        public ShotSound sound = ShotSound.Pistol;
    }
}
