// 第6マイルストーン：手応えの確認。カメラの反動の戻り、武器ごとの操作感の違い、ヒットストップの強弱。
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Matoi.AI;
using Matoi.CameraSystem;
using Matoi.Characters;
using Matoi.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Matoi.Tests
{
    public class CombatFeelTests : InputTestFixture
    {
        Gamepad pad;
        GameObject player;
        CharacterMotor motor;
        WeaponHolder holder;
        WeaponShooter shooter;
        MeleeAttack melee;
        ThirdPersonCameraController tp;

        IEnumerator Load()
        {
            pad = InputSystem.AddDevice<Gamepad>();
            SceneManager.LoadScene("TestField");
            yield return null;
            yield return null;
            player = GameObject.FindWithTag("Player");
            motor = player.GetComponent<CharacterMotor>();
            holder = player.GetComponent<WeaponHolder>();
            shooter = player.GetComponent<WeaponShooter>();
            melee = player.GetComponent<MeleeAttack>();
            Object.FindAnyObjectByType<CameraModeSwitcher>().SetMode(CameraMode.ThirdPerson);
            tp = ThirdPersonCameraController.Instance;
            Time.timeScale = 1f;
            motor.Teleport(new Vector3(1.5f, 0.1f, -2f), 0f);
            tp.SetActive(true);
            foreach (var e in EnemyBrain.All.ToArray()) e.PlaceAt(new Vector3(40f, 0.1f, 40f), 0f);
            yield return new WaitForSeconds(0.6f);
        }

        IEnumerator Tap(ButtonControl button, float after = 0.2f)
        {
            Press(button);
            yield return null;
            yield return null;
            Release(button);
            yield return new WaitForSeconds(after);
        }

        /// <summary>構えて 1 発撃ち、跳ね上がりの量と、元に戻るまでの時間を測る。</summary>
        IEnumerator MeasureSingleShot(System.Action<float, float> result)
        {
            Press(pad.leftTrigger);
            yield return new WaitForSeconds(0.6f);
            int before = shooter.ShotsFired;
            Press(pad.rightTrigger);
            yield return new WaitUntil(() => shooter.ShotsFired > before);
            Release(pad.rightTrigger);
            yield return null;
            float kick = tp.RecoilAmount;
            float start = Time.time;
            while (tp.RecoilAmount > 0.01f && Time.time < start + 3f) yield return null;
            float recovered = Time.time - start;
            Release(pad.leftTrigger);
            yield return new WaitForSeconds(0.3f);
            result(kick, recovered);
        }

        [UnityTest]
        public IEnumerator RifleBurst_ClimbsThenReturnsToTheOriginalAim()
        {
            yield return Load();
            yield return Tap(pad.rightShoulder);
            tp.Pitch = 5f;
            Press(pad.leftTrigger);
            yield return new WaitForSeconds(0.6f);
            float aim = tp.ViewPitch;
            Press(pad.rightTrigger);
            yield return new WaitForSeconds(0.8f);
            float peak = tp.RecoilAmount;
            Release(pad.rightTrigger);
            yield return new WaitForSeconds(1.6f);
            Release(pad.leftTrigger);
            Debug.Log($"[MatoiTest] rifle climb: peak={peak:0.0}deg left={tp.RecoilAmount:0.00} aimDiff={Mathf.Abs(tp.ViewPitch - aim):0.00}");
            Assert.That(peak, Is.InRange(2f, 7.1f), "連射の跳ね上がりの量が想定外");
            Assert.Less(tp.RecoilAmount, 0.05f, "撃ち終わってもカメラが元に戻らない");
            Assert.AreEqual(aim, tp.ViewPitch, 0.1f, "元の狙いの高さに戻っていない");
        }

        [UnityTest]
        public IEnumerator Pistol_KicksHarderPerShot_ButRecoversFasterThanRifle()
        {
            yield return Load();
            float pistolKick = 0f, pistolTime = 0f, rifleKick = 0f, rifleTime = 0f;
            yield return MeasureSingleShot((k, t) => { pistolKick = k; pistolTime = t; });
            yield return Tap(pad.rightShoulder, 0.5f);
            Assert.AreEqual(WeaponStance.Rifle, holder.CurrentWeapon.stance, "前提：ライフルを持っている");
            yield return MeasureSingleShot((k, t) => { rifleKick = k; rifleTime = t; });
            Debug.Log($"[MatoiTest] weapon kick: pistol={pistolKick:0.00}deg/{pistolTime:0.00}s rifle={rifleKick:0.00}deg/{rifleTime:0.00}s");
            Assert.Greater(pistolKick, rifleKick * 1.5f, "拳銃の 1 発の跳ね上がりがライフルより大きくない");
            Assert.Less(pistolTime, rifleTime, "拳銃の戻りがライフルより速くない");
        }

        [UnityTest]
        public IEnumerator Rifle_ZoomsCloser_AndMovesSlowerWhileAiming()
        {
            yield return Load();
            Press(pad.leftTrigger);
            Set(pad.leftStick, new Vector2(0f, 1f));
            yield return new WaitForSeconds(1.2f);
            float pistolFov = Camera.main.fieldOfView, pistolSpeed = motor.Speed;
            Release(pad.leftTrigger);
            Set(pad.leftStick, Vector2.zero);
            yield return new WaitForSeconds(0.5f);

            motor.Teleport(new Vector3(1.5f, 0.1f, -2f), 0f);
            yield return Tap(pad.rightShoulder, 0.5f);
            Press(pad.leftTrigger);
            Set(pad.leftStick, new Vector2(0f, 1f));
            yield return new WaitForSeconds(1.2f);
            float rifleFov = Camera.main.fieldOfView, rifleSpeed = motor.Speed;
            Release(pad.leftTrigger);
            Set(pad.leftStick, Vector2.zero);

            Debug.Log($"[MatoiTest] weapon handling: pistol fov={pistolFov:0.0} speed={pistolSpeed:0.00} / rifle fov={rifleFov:0.0} speed={rifleSpeed:0.00}");
            Assert.Less(rifleFov, pistolFov - 3f, "ライフルの構えが拳銃より寄っていない");
            Assert.Less(rifleSpeed, pistolSpeed * 0.85f, "ライフルの構え移動が拳銃より遅くない");
        }

        [UnityTest]
        public IEnumerator ComboHits_HaveStrongerHitStopEachStep()
        {
            yield return Load();
            var enemy = EnemyBrain.All.OrderBy(x => x.name).First();
            enemy.enabled = false;
            enemy.GetComponent<Health>().Configure(1000f, Faction.Hostile);
            enemy.PlaceAt(motor.transform.position + motor.transform.forward * 1.1f, 180f);
            enemy.enabled = false;
            yield return new WaitForSeconds(0.2f);

            var stops = new List<float>();
            melee.Struck += count => { if (count > 0) stops.Add(HitStop.LastDuration); };
            float end = Time.time + 2.4f;
            float nextPress = Time.time;
            while (Time.time < end && stops.Count < 3)
            {
                if (Time.time >= nextPress) { Press(pad.buttonEast); yield return null; Release(pad.buttonEast); nextPress = Time.time + 0.22f; }
                enemy.transform.position = motor.transform.position + motor.transform.forward * 1.1f;
                yield return null;
            }
            Debug.Log("[MatoiTest] combo hit stop: " + string.Join(", ", stops.Select(s => s.ToString("0.000"))));
            Assert.AreEqual(3, stops.Count, "コンボが 3 回当たっていない");
            Assert.Less(stops[0], stops[1], "2 段目のヒットストップが 1 段目より長くない");
            Assert.Less(stops[1], stops[2], "3 段目のヒットストップが 2 段目より長くない");
        }

        [UnityTest]
        public IEnumerator GunHit_OnlyStopsTimeOnTheKillingShot()
        {
            yield return Load();
            yield return Tap(pad.rightShoulder, 0.5f);
            var enemy = EnemyBrain.All.OrderBy(x => x.name).First();
            var health = enemy.GetComponent<Health>();
            health.Configure(1000f, Faction.Hostile);
            enemy.PlaceAt(motor.transform.position + Vector3.forward * 6f, 180f);
            enemy.enabled = false;
            yield return new WaitForSeconds(0.3f);

            Vector3 chest = enemy.transform.position + Vector3.up * 1.2f;
            Vector3 d = chest - Camera.main.transform.position;
            tp.Yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            tp.Pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            Press(pad.leftTrigger);
            yield return new WaitForSeconds(0.7f);

            int stopsBefore = HitStop.Count;
            float hpBefore = health.Current;
            Press(pad.rightTrigger);
            yield return new WaitUntil(() => health.Current < hpBefore);
            Release(pad.rightTrigger);
            yield return new WaitForSeconds(0.15f);
            int stopsAfterHit = HitStop.Count;

            health.Configure(1f, Faction.Hostile);
            float wait = Time.time + 2f;
            Press(pad.rightTrigger);
            yield return new WaitUntil(() => health.IsDead || Time.time > wait);
            Release(pad.rightTrigger);
            yield return null;
            int stopsAfterKill = HitStop.Count;
            Release(pad.leftTrigger);

            Debug.Log($"[MatoiTest] gun hit stop: hit={stopsAfterHit - stopsBefore} kill={stopsAfterKill - stopsAfterHit} dead={health.IsDead}");
            Assert.AreEqual(stopsBefore, stopsAfterHit, "普通の命中で時間が止まっている");
            Assert.IsTrue(health.IsDead, "前提：敵を倒している");
            Assert.Greater(stopsAfterKill, stopsAfterHit, "敵を倒した瞬間にヒットストップが起きていない");
        }
    }
}
