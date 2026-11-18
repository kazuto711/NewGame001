using Matoi.Characters;
using UnityEngine;

namespace Matoi.AI
{
    /// <summary>
    /// 敵の種類ごとの性能データ。敵を増やす時はこのファイルを複製して数値と見た目を変える。
    /// Project ウィンドウで右クリック → Create → Matoi → Enemy Definition。
    /// </summary>
    [CreateAssetMenu(menuName = "Matoi/Enemy Definition", fileName = "Enemy")]
    public class EnemyDefinition : ScriptableObject
    {
        [Header("基本")]
        public string displayName = "侵食者";
        public float maxHealth = 90f;
        public MovementProfile movement;

        [Header("索敵")]
        [Tooltip("プレイヤーに気づく距離 (m)")]
        public float detectRange = 13f;
        [Tooltip("見えていなくても気づく距離（足音など）")]
        public float hearRange = 4f;
        [Tooltip("追うのをあきらめる距離")]
        public float loseRange = 28f;
        [Tooltip("撃たれたら、見えていなくても追いかける")]
        public bool aggroOnDamage = true;

        [Header("ステルス（プレイヤーがしゃがんでいる時）")]
        [Tooltip("視野の広さ（度）。この範囲の外にいるプレイヤーは見えない")]
        public float sneakFieldOfView = 110f;
        [Tooltip("しゃがんだプレイヤーが見える距離（detectRange に掛ける割合）")]
        [Range(0.2f, 1f)] public float sneakSightScale = 0.7f;
        [Tooltip("近くで見ている時に、警戒度が満タンになるまでの秒数")]
        public float sneakDetectTime = 0.8f;
        [Tooltip("警戒度が 0 に戻るまでの秒数")]
        public float awarenessDecayTime = 4f;
        [Tooltip("怪しい場所を調べる時間（秒）")]
        public float searchTime = 3f;
        [Tooltip("しゃがんだプレイヤーを見失ってから、あきらめるまでの秒数")]
        public float giveUpTime = 4f;

        [Header("追跡")]
        [Tooltip("この距離より遠いと走る（ダッシュ）")]
        public float sprintDistance = 7f;

        [Header("攻撃")]
        [Tooltip("攻撃を始める距離")]
        public float attackRange = 1.7f;
        [Tooltip("攻撃の前の「ため」の時間。プレイヤーが避ける合図になる")]
        public float windupTime = 0.45f;
        [Tooltip("攻撃後に次の攻撃までの最短時間")]
        public float attackCooldown = 1.1f;
        public float attackDamage = 15f;
        public float attackKnockback = 4f;
        public float attackLunge = 3f;

        [Header("被弾")]
        public float staggerTime = 0.35f;
        [Tooltip("一度ひるんだら、この秒数は射撃でひるまない（強い敵ほど長く）")]
        public float staggerCooldown = 0.8f;
        [Tooltip("吹き飛びやすさ（重い敵ほど小さく）")]
        public float knockbackScale = 1f;

        [Header("テスト用")]
        [Tooltip("倒されてから元の場所に復活するまで（秒）。0 以下で復活しない")]
        public float respawnDelay = 8f;
    }
}
