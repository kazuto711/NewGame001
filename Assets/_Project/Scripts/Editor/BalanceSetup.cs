// M8: 戦闘の数値調整をまとめて適用するツール（メニュー「Matoi > Apply Combat Balance」）。
// 調整の意図をここに残しておき、後から見返せるようにする。値は各データファイルに書き込まれるので、
// その後は Inspector で直接いじってよい（このツールを再実行すると上書きされる）。
using Matoi.AI;
using Matoi.Combat;
using UnityEditor;
using UnityEngine;

namespace Matoi.EditorTools
{
    public static class BalanceSetup
    {
        [MenuItem("Matoi/Apply Combat Balance (M8 数値調整)")]
        public static void Apply()
        {
            // 拳銃：弾無限の「頼れるサブ」。単発で正確、1体を約6発（1.2秒〜）
            var pistol = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Project/Data/Weapons/Weapon_Pistol.asset");
            if (pistol)
            {
                pistol.damage = 20f;
                pistol.fireRate = 5f;
                pistol.spread = 0.8f;
                pistol.magazineSize = 12;
                pistol.reserveAmmo = -1;
                pistol.knockback = 1.0f;
                EditorUtility.SetDirty(pistol);
            }

            // ライフル：強いが弾が有限。1体を約5発（0.6秒〜）、移動撃ちはばらつく
            var rifle = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Project/Data/Weapons/Weapon_AK47.asset");
            if (rifle)
            {
                rifle.damage = 24f;
                rifle.fireRate = 9f;
                rifle.spread = 1.8f;
                rifle.moveSpread = 5f;
                rifle.magazineSize = 30;
                rifle.reserveAmmo = 90;
                rifle.reloadTime = 1.9f;
                EditorUtility.SetDirty(rifle);
            }

            // 侵食者：体力を上げ、射撃でのひるみに間隔を設けて「押してくる」圧を出す
            var enemy = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/Enemy_Infected.asset");
            if (enemy)
            {
                enemy.maxHealth = 110f;
                enemy.staggerTime = 0.3f;
                enemy.staggerCooldown = 0.8f;
                enemy.windupTime = 0.5f;      // 回避が間に合う長さ
                enemy.attackCooldown = 1.2f;
                enemy.attackDamage = 15f;
                enemy.detectRange = 14f;
                EditorUtility.SetDirty(enemy);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Matoi] Combat balance applied.");
        }
    }
}
