using Matoi.Combat;
using UnityEngine;

namespace Matoi.Items
{
    public enum ItemCategory
    {
        Consumable,   // 使うと回復する
        Ammo,         // 使うと弾が増える
        Material,     // 素材（将来のクラフト用）
        Valuable,     // 換金品・収集品
        Key,          // ミッションで使う大切な物（捨てられない）
    }

    /// <summary>
    /// アイテム 1 種類のデータ。増やす時はこのファイルを複製して値を変える。
    /// Project ウィンドウで右クリック → Create → Matoi → Item。
    /// </summary>
    [CreateAssetMenu(menuName = "Matoi/Item", fileName = "Item")]
    public class ItemDefinition : ScriptableObject
    {
        public string displayName = "アイテム";
        [TextArea] public string description;
        public ItemCategory category = ItemCategory.Material;
        [Tooltip("1 個の重さ (kg)")]
        public float weight = 0.5f;

        [Header("使った時の効果")]
        [Tooltip("回復する体力")]
        public float heal;
        [Tooltip("弾を補充する武器")]
        public WeaponDefinition ammoFor;
        public int ammoAmount;

        [Header("地面に置いた時の見た目")]
        public GameObject worldModel;

        public bool IsUsable => heal > 0f || (ammoFor && ammoAmount > 0);
    }
}
