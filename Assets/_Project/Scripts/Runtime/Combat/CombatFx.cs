using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Matoi.Combat
{
    /// <summary>
    /// 戦闘の見た目の演出（マズルフラッシュ・弾道の光・着弾の火花・効果音）を出す。
    /// 素材（材質）はシーンに 1 つ置いたこの部品に設定し、どこからでも CombatFx.Instance で使う。
    /// </summary>
    public class CombatFx : MonoBehaviour
    {
        public static CombatFx Instance { get; private set; }

        [SerializeField] Material flashMaterial;
        [SerializeField] Material tracerMaterial;
        [SerializeField] Material sparkMaterial;
        [SerializeField] float tracerTime = 0.06f;

        readonly Queue<LineRenderer> tracers = new Queue<LineRenderer>();
        ParticleSystem sparks, dust;
        AudioSource audioSource;

        void Awake()
        {
            Instance = this;
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 0f;
            sparks = MakeParticles("Sparks", new Color(1f, 0.75f, 0.35f), 0.04f, 7f, 0.25f, 0.5f);
            dust = MakeParticles("Dust", new Color(0.55f, 0.5f, 0.42f, 0.8f), 0.12f, 2.5f, 0.45f, 0.3f);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public void PlaySound(AudioClip clip, float volume = 1f, float pitchJitter = 0.08f)
        {
            if (!clip) return;
            audioSource.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            audioSource.PlayOneShot(clip, volume);
        }

        public void MuzzleFlash(Transform muzzle, float size = 1f) => StartCoroutine(FlashRoutine(muzzle, size));

        IEnumerator FlashRoutine(Transform muzzle, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(muzzle, false);
            go.transform.localScale = new Vector3(0.05f, 0.05f, 0.14f) * size / Mathf.Max(0.01f, muzzle.lossyScale.x);
            go.transform.localPosition = new Vector3(0, 0, 0.06f * size / Mathf.Max(0.01f, muzzle.lossyScale.x));
            go.transform.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = flashMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.7f, 0.35f);
            light.range = 3.5f;
            light.intensity = 2.5f;
            light.shadows = LightShadows.None;
            yield return new WaitForSeconds(0.04f);
            Destroy(go);
        }

        public void Tracer(Vector3 from, Vector3 to)
        {
            var lr = tracers.Count > 0 && !tracers.Peek().enabled ? tracers.Dequeue() : NewTracer();
            lr.enabled = true;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            StartCoroutine(HideTracer(lr));
        }

        LineRenderer NewTracer()
        {
            var go = new GameObject("Tracer");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.widthMultiplier = 0.025f;
            lr.sharedMaterial = tracerMaterial;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.startColor = new Color(1f, 1f, 1f, 0.2f);
            lr.endColor = Color.white;
            return lr;
        }

        IEnumerator HideTracer(LineRenderer lr)
        {
            yield return new WaitForSeconds(tracerTime);
            lr.enabled = false;
            tracers.Enqueue(lr);
        }

        /// <summary>着弾の演出。硬い物は火花、柔らかい物（的・地面）は土ぼこり。</summary>
        public void Impact(Vector3 point, Vector3 normal, bool soft)
        {
            var ps = soft ? dust : sparks;
            ps.transform.SetPositionAndRotation(point, Quaternion.LookRotation(normal));
            ps.Emit(soft ? 10 : 8);
        }

        ParticleSystem MakeParticles(string name, Color color, float size, float speed, float life, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.5f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startColor = color;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.02f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = sparkMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            main.loop = true;
            ps.Play(); // 放出はしない状態で再生し続け、Emit() で必要な時だけ出す
            return ps;
        }
    }
}
