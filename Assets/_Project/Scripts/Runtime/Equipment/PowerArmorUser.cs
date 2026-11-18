using System;
using Matoi.CameraSystem;
using Matoi.Characters;
using Matoi.Combat;
using UnityEngine;

namespace Matoi.Equipment
{
    /// <summary>
    /// パワーアーマーへの乗り降りと、乗っている間の「別の身体」への切り替えを担当する。
    ///  - 見た目：骨に付けた装甲部品を表示し、体を一回り大きく
    ///  - 動き：重い移動プロファイル、回避は無敵のない「ブーストステップ」
    ///  - 戦闘：被ダメージの大半を装甲が肩代わり、ひるまない、パンチが重い、走って体当たり
    /// 乗る前の値を覚えておき、降りたら元に戻す。
    /// </summary>
    public class PowerArmorUser : MonoBehaviour
    {
        [SerializeField] GameObject[] armorPieces;
        [SerializeField] Renderer[] hideWhenArmored;   // 髪・ひげなど、ヘルメットからはみ出す物
        [SerializeField] Transform visualRoot;
        [SerializeField] MovementProfile armorProfile;

        [Header("乗っている時の体")]
        [SerializeField] float armoredScale = 1.12f;
        [SerializeField] float armoredHeight = 2.05f;
        [SerializeField] float armoredRadius = 0.45f;
        [Tooltip("被ダメージのうち装甲が肩代わりする割合")]
        [SerializeField, Range(0f, 1f)] float absorb = 0.8f;
        [Tooltip("装甲が壊れた一撃で本人が受けるダメージの上限")]
        [SerializeField] float breakPilotDamageCap = 25f;

        [Header("乗っている時の近接攻撃（重いパンチ）")]
        [SerializeField] float punchDamage = 70f;
        [SerializeField] float punchKnockback = 14f;
        [SerializeField] float punchReach = 1.25f;
        [SerializeField] float punchRadius = 1.0f;

        CharacterMotor motor;
        CharacterController cc;
        Health health;
        HitReaction reaction;
        MeleeAttack melee;
        DodgeRoll dodge;
        PowerArmorFootsteps footsteps;
        PowerArmorRam ram;

        // 乗る前の値（降りる時に戻す）
        MovementProfile humanProfile;
        float h0, r0, c0, stagger0, knock0;
        float mDamage, mKnock, mReach, mRadius, mHitStop, mShake;
        bool mCombo;
        float dSpeed, dDuration;
        Vector2 dWindow;

        public bool IsArmored { get; private set; }
        public PowerArmorStation Current { get; private set; }
        public float Integrity => Current ? Current.Integrity : 0f;
        public int BreakCount { get; private set; }
        public event Action<bool> ArmorChanged;

        /// <summary>選ばれた主人公のモデルに付いた装甲部品を使う。</summary>
        public void SetModelParts(GameObject[] pieces, Renderer[] hidden)
        {
            armorPieces = pieces;
            hideWhenArmored = hidden;
        }

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            cc = GetComponent<CharacterController>();
            health = GetComponent<Health>();
            reaction = GetComponent<HitReaction>();
            melee = GetComponent<MeleeAttack>();
            dodge = GetComponent<DodgeRoll>();
            footsteps = GetComponent<PowerArmorFootsteps>();
            ram = GetComponent<PowerArmorRam>();
            SetPiecesVisible(false);
            if (footsteps) footsteps.enabled = false;
            if (ram) ram.enabled = false;
        }

        /// <summary>近くのアーマーに乗る／乗っていれば降りる。何もしなかったら false。</summary>
        public bool TryInteract()
        {
            if (health && health.IsDead) return false;
            if (IsArmored) { Exit(false); return true; }
            var s = NearestAvailable();
            if (!s) return false;
            Enter(s);
            return true;
        }

        public PowerArmorStation NearestAvailable()
        {
            PowerArmorStation best = null;
            float bestD = float.MaxValue;
            foreach (var s in PowerArmorStation.All)
            {
                if (!s.CanEnter(transform.position)) continue;
                float d = Vector3.Distance(transform.position, s.transform.position);
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        public void Enter(PowerArmorStation station)
        {
            if (IsArmored || station == null || station.Occupied) return;
            Current = station;
            IsArmored = true;
            station.SetOccupied(true);
            motor.Teleport(station.transform.position + Vector3.up * 0.05f, station.transform.eulerAngles.y);

            humanProfile = motor.Profile;
            if (armorProfile) motor.SetProfile(armorProfile);
            if (visualRoot) visualRoot.localScale = Vector3.one * armoredScale;
            h0 = cc.height; r0 = cc.radius; c0 = cc.center.y;
            cc.height = armoredHeight; cc.radius = armoredRadius; cc.center = new Vector3(0, armoredHeight / 2f, 0);

            if (reaction)
            {
                stagger0 = reaction.staggerThreshold; knock0 = reaction.knockbackScale;
                reaction.staggerThreshold = 9999f;   // ひるまない
                reaction.knockbackScale = 0.15f;      // ほとんど押されない
            }
            if (melee)
            {
                mDamage = melee.damage; mKnock = melee.knockback; mReach = melee.reach; mRadius = melee.radius;
                mHitStop = melee.hitStop; mShake = melee.shake;
                mCombo = melee.comboEnabled;
                melee.comboEnabled = false;   // アーマーの拳は 1 発ごとに重い単発
                melee.damage = punchDamage; melee.knockback = punchKnockback; melee.reach = punchReach;
                melee.radius = punchRadius; melee.hitStop = 0.11f; melee.shake = 0.7f;
            }
            if (dodge)
            {
                dSpeed = dodge.speed; dDuration = dodge.duration; dWindow = dodge.invulnerableWindow;
                dodge.speed = 6.5f; dodge.duration = 0.35f; dodge.invulnerableWindow = Vector2.zero; // 無敵なしのブーストステップ
            }
            health.DamageFilter = Absorb;
            SetPiecesVisible(true);
            if (footsteps) footsteps.enabled = true;
            if (ram) ram.enabled = true;

            CameraShake.Shake(0.6f);
            CombatFx.Instance?.PlaySound(ProceduralSfx.PowerUp, 0.8f, 0.02f);
            CombatFx.Instance?.Impact(transform.position + Vector3.up * 0.1f, Vector3.up, true);
            ArmorChanged?.Invoke(true);
        }

        public void Exit(bool broken)
        {
            if (!IsArmored) return;
            var station = Current;
            IsArmored = false;
            Current = null;

            // アーマーはその場に残し、自分は背中側から降りる
            Vector3 pos = transform.position;
            Quaternion rot = Quaternion.Euler(0, transform.eulerAngles.y, 0);
            station.ParkAt(pos, rot);
            station.SetOccupied(false);
            if (broken) station.Integrity = 0f;

            if (humanProfile) motor.SetProfile(humanProfile);
            if (visualRoot) visualRoot.localScale = Vector3.one;
            cc.height = h0; cc.radius = r0; cc.center = new Vector3(0, c0, 0);
            if (reaction) { reaction.staggerThreshold = stagger0; reaction.knockbackScale = knock0; }
            if (melee)
            {
                melee.damage = mDamage; melee.knockback = mKnock; melee.reach = mReach; melee.radius = mRadius;
                melee.hitStop = mHitStop; melee.shake = mShake;
                melee.comboEnabled = mCombo;
            }
            if (dodge) { dodge.speed = dSpeed; dodge.duration = dDuration; dodge.invulnerableWindow = dWindow; }
            health.DamageFilter = null;
            SetPiecesVisible(false);
            if (footsteps) footsteps.enabled = false;
            if (ram) ram.enabled = false;

            motor.Teleport(pos - transform.forward * 1.3f + Vector3.up * 0.05f, transform.eulerAngles.y);
            if (broken)
            {
                BreakCount++;
                motor.AddImpulse(-transform.forward * 5f);
                CameraShake.Shake(0.8f);
                CombatFx.Instance?.Impact(pos + Vector3.up * 1.2f, Vector3.up, false);
            }
            ArmorChanged?.Invoke(false);
        }

        DamageInfo Absorb(DamageInfo info)
        {
            if (!IsArmored || !Current) return info;
            float taken = info.amount * absorb;
            Current.Integrity -= taken;
            info.amount -= taken;
            if (Current.Integrity <= 0f)
            {
                // 装甲が壊れた：この一撃は装甲が身代わりになり、本人へのダメージは上限まで。
                // （壊れた瞬間に即死しないように。その後は生身で戦うことになる）
                info.amount = Mathf.Min(info.amount, breakPilotDamageCap);
                StartCoroutine(BreakNextFrame());
            }
            return info;
        }

        System.Collections.IEnumerator BreakNextFrame()
        {
            yield return null;
            if (IsArmored) Exit(true);
        }

        void SetPiecesVisible(bool v)
        {
            if (armorPieces != null) foreach (var p in armorPieces) if (p) p.SetActive(v);
            if (hideWhenArmored != null) foreach (var r in hideWhenArmored) if (r) r.enabled = !v;
        }

        GUIStyle style;

        void OnGUI()
        {
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            string text = null;
            if (IsArmored)
                text = $"アーマー耐久 {Mathf.CeilToInt(Integrity)} / {Current.maxIntegrity:0}　　E / ↑ : 降りる";
            else if (NearestAvailable() is { } s)
                text = $"E / ↑ : パワーアーマーに乗る（耐久 {s.IntegrityFraction * 100f:0}%）";
            if (text == null) return;
            var r = new Rect(0, Screen.height - 90, Screen.width, 40);
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
            GUI.color = IsArmored && Current && Current.IntegrityFraction < 0.3f ? new Color(1f, 0.4f, 0.3f) : new Color(1f, 0.9f, 0.6f);
            GUI.Label(r, text, style);
        }
    }
}
