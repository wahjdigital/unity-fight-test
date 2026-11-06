using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Dust, debris, fire and smoke: particle systems built in code with the soft puff texture.</summary>
public static class Fx
{
    static int _lights;
    public static void Reset()
    {
        _lights = 0;
        _dust = _debris = _sparks = null;
    }

    static ParticleSystem Make(string name, Vector3 at, string material, out ParticleSystemRenderer r)
    {
        var go = new GameObject(name);
        go.transform.position = at;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = Battle.Mat(material);
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        return ps;
    }

    static Gradient Fade(Color a, Color b, float startAlpha = 1f)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 1) },
            new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(startAlpha, 0.08f), new GradientAlphaKey(startAlpha * 0.6f, 0.5f), new GradientAlphaKey(0, 1) });
        return g;
    }

    // three shared emitters for everything short lived: making a particle system per hit costs too much on phones
    static ParticleSystem _dust, _debris, _sparks;

    static ParticleSystem Shared(ref ParticleSystem ps, string name, string material, System.Action<ParticleSystem, ParticleSystemRenderer> setup)
    {
        if (ps != null) return ps;
        ps = Make(name, Vector3.zero, material, out var r);
        var main = ps.main;
        main.loop = false;
        main.maxParticles = 3000;
        var em = ps.emission;
        em.enabled = false;
        setup(ps, r);
        ps.Play();
        return ps;
    }

    /// <summary>A cloud of dust rolling out over the ground.</summary>
    public static void Dust(Vector3 at, float size, int count = 30)
    {
        var ps = Shared(ref _dust, "dust", "Dust", (p, r) =>
        {
            var main = p.main;
            main.gravityModifier = -0.02f;
            var vel = p.limitVelocityOverLifetime;
            vel.enabled = true;
            vel.dampen = 0.12f;
            vel.limit = 0.4f;
            var col = p.colorOverLifetime;
            col.enabled = true;
            col.color = Fade(Color.white, new Color(0.9f, 0.88f, 0.85f), 0.75f);
            var sz = p.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.5f, 1, 1.5f));
            var rot = p.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
        });
        var ep = new ParticleSystem.EmitParams { startColor = new Color(0.76f, 0.67f, 0.52f, 1f) };
        for (var i = 0; i < count; i++)
        {
            var dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y) * 0.6f;
            ep.position = at + Vector3.up * 0.2f + new Vector3(dir.x, 0, dir.z) * 0.8f * size * Random.value;
            ep.velocity = dir.normalized * Random.Range(0.8f, 2.6f) * size;
            ep.startSize = Random.Range(1.4f, 3.2f) * size;
            ep.startLifetime = Random.Range(1.6f, 3.2f);
            ep.rotation = Random.Range(0f, 360f);
            ps.Emit(ep, 1);
        }
    }

    /// <summary>Splinters and stones thrown up by a hit or a collapse.</summary>
    public static void Debris(Vector3 at, float size, int count, Color tint)
    {
        var ps = Shared(ref _debris, "debris", "Debris", (p, r) =>
        {
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            r.shadowCastingMode = ShadowCastingMode.On;
            var main = p.main;
            main.startSize3D = true;
            main.startRotation3D = true;
            main.gravityModifier = 1.6f;
            var rot = p.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-6, 6);
            rot.y = new ParticleSystem.MinMaxCurve(-6, 6);
            rot.z = new ParticleSystem.MinMaxCurve(-6, 6);
            var coll = p.collision;
            coll.enabled = true;
            coll.type = ParticleSystemCollisionType.Planes;
            var plane = new GameObject("floor").transform;
            plane.SetParent(p.transform, false);
            plane.position = new Vector3(0, 0.02f, 0);
            coll.SetPlane(0, plane);
            coll.dampen = 0.6f;
            coll.bounce = 0.25f;
            coll.lifetimeLoss = 0f;
            var sz = p.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.6f));
        });
        var ep = new ParticleSystem.EmitParams();
        for (var i = 0; i < count; i++)
        {
            var dir = Quaternion.Euler(Random.Range(-40f, 40f), 0, Random.Range(-40f, 40f)) * Vector3.up;
            ep.position = at + Random.insideUnitSphere * 0.3f * size;
            ep.velocity = dir * Random.Range(3f, 7f) * size;
            ep.startSize3D = new Vector3(Random.Range(0.08f, 0.3f), Random.Range(0.06f, 0.2f), Random.Range(0.08f, 0.35f)) * size;
            ep.rotation3D = new Vector3(Random.Range(0, 360f), Random.Range(0, 360f), Random.Range(0, 360f));
            ep.startLifetime = Random.Range(1.2f, 2.2f);
            ep.startColor = tint * Random.Range(0.7f, 1f);
            ps.Emit(ep, 1);
        }
    }

    /// <summary>Small bright sparks where steel meets wood or stone.</summary>
    public static void Sparks(Vector3 at, int count = 8)
    {
        var ps = Shared(ref _sparks, "sparks", "Fire", (p, r) =>
        {
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.04f;
            r.lengthScale = 1.5f;
            var main = p.main;
            main.gravityModifier = 1f;
        });
        var ep = new ParticleSystem.EmitParams { startColor = new Color(1f, 0.75f, 0.35f, 1f) };
        for (var i = 0; i < count; i++)
        {
            ep.position = at;
            ep.velocity = Random.onUnitSphere * Random.Range(3f, 7f);
            ep.startSize = Random.Range(0.05f, 0.1f);
            ep.startLifetime = Random.Range(0.15f, 0.4f);
            ps.Emit(ep, 1);
        }
    }

    /// <summary>A burning fire with a column of smoke over it; lives until destroyed.</summary>
    public static GameObject Fire(Vector3 at, float size)
    {
        var root = new GameObject("fire");
        root.transform.position = at;

        var fire = Make("flames", at, "Fire", out _);
        fire.transform.SetParent(root.transform, true);
        var main = fire.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f * size, 1.6f * size);
        main.startSize = new ParticleSystem.MinMaxCurve(0.7f * size, 1.4f * size);
        main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.18f), new Color(1f, 0.8f, 0.35f));
        main.gravityModifier = -0.25f;
        var em = fire.emission;
        em.rateOverTime = 18 * size;
        var shape = fire.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.6f * size;
        shape.rotation = new Vector3(-90, 0, 0);
        var col = fire.colorOverLifetime;
        col.enabled = true;
        col.color = Fade(new Color(1f, 0.9f, 0.6f), new Color(0.8f, 0.25f, 0.05f), 1f);
        var sz = fire.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1f, 1, 0.2f));
        fire.Play();

        var smoke = Make("smoke", at + Vector3.up * 1.2f * size, "Smoke", out _);
        smoke.transform.SetParent(root.transform, true);
        var sm = smoke.main;
        sm.loop = true;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        sm.startSize = new ParticleSystem.MinMaxCurve(1.0f * size, 1.9f * size);
        sm.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        sm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.22f, 0.2f, 0.19f), new Color(0.38f, 0.36f, 0.34f));
        sm.gravityModifier = -0.06f;
        var sem = smoke.emission;
        sem.rateOverTime = 4.5f * size;
        var sshape = smoke.shape;
        sshape.shapeType = ParticleSystemShapeType.Circle;
        sshape.radius = 0.5f * size;
        sshape.rotation = new Vector3(-90, 0, 0);
        var svel = smoke.velocityOverLifetime;
        svel.enabled = true;
        svel.space = ParticleSystemSimulationSpace.World;
        svel.x = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        svel.y = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
        svel.z = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
        var scol = smoke.colorOverLifetime;
        scol.enabled = true;
        scol.color = Fade(Color.white, Color.white, 0.7f);
        var ssz = smoke.sizeOverLifetime;
        ssz.enabled = true;
        ssz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.6f, 1, 2.4f));
        var srot = smoke.rotationOverLifetime;
        srot.enabled = true;
        srot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
        smoke.Play();

        // the flames light the ground and the soldiers around them (a few lights at most, they cost on phones)
        if (_lights >= 4) return root;
        _lights++;
        var light = new GameObject("glow").AddComponent<Light>();
        light.transform.SetParent(root.transform, false);
        light.transform.localPosition = Vector3.up * 0.8f * size;
        light.type = LightType.Point;
        light.color = new Color(1f, 0.55f, 0.2f);
        light.range = 5f * size;
        light.intensity = 2.5f;
        light.shadows = LightShadows.None;
        root.AddComponent<Flicker>().Light = light;
        return root;
    }

    /// <summary>A golden swirl around the hero when he rallies the troops.</summary>
    public static void Aura(Transform follow, float duration)
    {
        var ps = Make("aura", follow.position, "Fire", out _);
        ps.transform.SetParent(follow, true);
        var main = ps.main;
        main.duration = duration;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        main.startColor = new Color(1f, 0.8f, 0.3f);
        main.gravityModifier = -0.3f;
        var em = ps.emission;
        em.rateOverTime = 40;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.9f;
        shape.rotation = new Vector3(-90, 0, 0);
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = Fade(new Color(1f, 0.95f, 0.7f), new Color(1f, 0.6f, 0.2f), 1f);
        ps.Play();
        Object.Destroy(ps.gameObject, duration + 1.5f);
    }

    /// <summary>A short golden ring that swells on the ground (a soldier landing, the hero rallying).</summary>
    public static void Ring(Vector3 at, float size, Color color, float life = 0.6f)
    {
        var go = Battle.Shape(PrimitiveType.Quad);
        go.transform.position = at + Vector3.up * 0.05f;
        go.transform.rotation = Quaternion.Euler(90, 0, 0);
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = new Material(Battle.Mat("Ring"));
        r.shadowCastingMode = ShadowCastingMode.Off;
        go.AddComponent<Swell>().Init(size, color, life);
    }
}

public class Flicker : MonoBehaviour
{
    public Light Light;
    float seed;

    void Start() => seed = Random.value * 100;

    void Update()
    {
        if (Light == null) return;
        Light.intensity = 2.2f + Mathf.PerlinNoise(seed, Time.time * 6f) * 1.4f;
    }
}

public class Swell : MonoBehaviour
{
    float size, life, t;
    Color color;
    Material mat;

    public void Init(float size, Color color, float life)
    {
        this.size = size;
        this.color = color;
        this.life = life;
        mat = GetComponent<MeshRenderer>().sharedMaterial;
        Update();
    }

    void Update()
    {
        t += Time.deltaTime;
        var k = Mathf.Clamp01(t / life);
        transform.localScale = Vector3.one * size * (0.4f + 0.9f * Mathf.Sqrt(k));
        var c = color;
        c.a = color.a * (1 - k);
        mat.SetColor("_BaseColor", c);
        if (k >= 1) Destroy(gameObject);
    }
}
