using Matoi.Characters;
using Matoi.Combat;
using Matoi.Equipment;
using Matoi.InputHandling;
using UnityEngine;

namespace Matoi.Items
{
    /// <summary>
    /// 近くの調べられる物を選び、ボタンの押下・長押しを処理する。
    /// パワーアーマーの近くや搭乗中は、今までどおりアーマーの操作を優先する。
    /// </summary>
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] float reach = 2.2f;
        [Tooltip("体の正面にある物を、この距離ぶん優先する (m)")]
        [SerializeField] float facingBonus = 0.8f;

        PlayerInputReader input;
        CharacterAnimator characterAnimator;
        CharacterMotor motor;
        WeaponHolder weapons;
        HitReaction reaction;
        PowerArmorUser armor;
        readonly RaycastHit[] hits = new RaycastHit[12];

        public Inventory Inventory { get; private set; }
        public Interactable Current { get; private set; }
        public bool IsHolding { get; private set; }
        /// <summary>長押しの進み具合（0〜1）。</summary>
        public float HoldProgress { get; private set; }

        void Awake()
        {
            Inventory = GetComponent<Inventory>();
            input = GetComponent<PlayerInputReader>();
            characterAnimator = GetComponent<CharacterAnimator>();
            motor = GetComponent<CharacterMotor>();
            weapons = GetComponent<WeaponHolder>();
            reaction = GetComponent<HitReaction>();
            armor = GetComponent<PowerArmorUser>();
        }

        bool Blocked => (armor && (armor.IsArmored || armor.NearestAvailable() != null))
                        || (reaction && (reaction.IsStaggered || reaction.Health.IsDead))
                        || (motor && motor.IsDashing);

        void Update()
        {
            if (IsHolding) { UpdateHold(); return; }
            Current = Blocked ? null : FindBest();
        }

        Interactable FindBest()
        {
            Interactable best = null;
            float bestScore = float.MaxValue;
            Vector3 eye = transform.position + Vector3.up * 1.1f;
            foreach (var target in Interactable.All)
            {
                if (!target || !target.CanInteract) continue;
                Vector3 to = target.Point - transform.position;
                if (Mathf.Abs(to.y) > 2f) continue;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist > reach) continue;
                float facing = dist > 0.01f ? Vector3.Dot(to / dist, transform.forward) : 1f;
                float score = dist - facing * facingBonus;
                if (score < bestScore && HasLineOfSight(eye, target)) { best = target; bestScore = score; }
            }
            return best;
        }

        /// <summary>壁の向こうの物は調べられない。</summary>
        bool HasLineOfSight(Vector3 eye, Interactable target)
        {
            Vector3 d = target.Point - eye;
            float length = d.magnitude;
            if (length < 0.05f) return true;
            int n = Physics.RaycastNonAlloc(eye, d / length, hits, length, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Transform t = hits[i].collider.transform;
                if (t.IsChildOf(transform) || t.IsChildOf(target.SightRoot)) continue;
                return false;
            }
            return true;
        }

        /// <summary>ボタンが押された時に呼ぶ。調べる物があれば true（アーマーの操作はしない）。</summary>
        public bool Press()
        {
            if (IsHolding || Blocked) return false;
            Current = FindBest();
            if (!Current) return false;
            if (Current.HoldTime <= 0f) { Current.Interact(this); Current = null; return true; }

            IsHolding = true;
            HoldProgress = 0f;
            if (weapons) weapons.SetDrawn(false);
            if (motor) motor.SetCrouch(false);
            if (characterAnimator) characterAnimator.PlayAction("Gather", Current.HoldTime);
            return true;
        }

        void UpdateHold()
        {
            bool held = input && input.InteractHeld;
            if (!held || !Current || !Current.CanInteract || Blocked) { CancelHold(); return; }
            HoldProgress += Time.deltaTime / Mathf.Max(0.05f, Current.HoldTime);
            if (HoldProgress < 1f) return;
            var target = Current;
            CancelHold();
            target.Interact(this);
        }

        void CancelHold()
        {
            IsHolding = false;
            HoldProgress = 0f;
            Current = null;
        }
    }
}
