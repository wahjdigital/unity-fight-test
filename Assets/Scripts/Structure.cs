using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>A building, an archer tower or one stretch of the palisade: it has a footprint, hit points and a ruin.</summary>
public class Structure : MonoBehaviour
{
    public string Key;
    public string Label;
    public bool IsWall, IsTower;
    public float MaxHp, Hp;
    public float Weight = 1;
    /// <summary>Footprint in world x (min.x..max.x) and z (min.y..max.y).</summary>
    public Vector2 Min, Max;
    public float Height = 5f;
    public Vector3 Normal;
    public MeshRenderer Picture;
    public GameObject Intact, Ruin, Occluder;
    public Vector3 FirePoint;

    public bool Alive => Hp > 0;
    public Vector3 Center => new((Min.x + Max.x) / 2, 0, (Min.y + Max.y) / 2);
    public float Size => Mathf.Max(Max.x - Min.x, Max.y - Min.y);

    float flash;
    GameObject fire;
    float towerCooldown = 1f;
    MaterialPropertyBlock block;

    public Vector3 ClosestPoint(Vector3 p) => new(Mathf.Clamp(p.x, Min.x, Max.x), 0, Mathf.Clamp(p.z, Min.y, Max.y));

    public float DistanceTo(Vector3 p)
    {
        var c = ClosestPoint(p);
        return new Vector2(p.x - c.x, p.z - c.z).magnitude;
    }

    public bool Contains(Vector3 p, float pad) => p.x > Min.x - pad && p.x < Max.x + pad && p.z > Min.y - pad && p.z < Max.y + pad;

    /// <summary>Pushes [p] out of the footprint (with [pad] of room) along the shortest way.</summary>
    public Vector3 PushOut(Vector3 p, float pad)
    {
        if (!Contains(p, pad)) return p;
        var dl = p.x - (Min.x - pad);
        var dr = Max.x + pad - p.x;
        var dd = p.z - (Min.y - pad);
        var du = Max.y + pad - p.z;
        var m = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(dd, du));
        if (m == dl) p.x = Min.x - pad;
        else if (m == dr) p.x = Max.x + pad;
        else if (m == dd) p.z = Min.y - pad;
        else p.z = Max.y + pad;
        return p;
    }

    public void Hit(float damage, Vector3 at, bool loud = true)
    {
        if (!Alive || Battle.Over) return;
        Hp -= damage;
        flash = 0.12f;
        if (loud)
        {
            Fx.Debris(at + Vector3.up * 0.8f, 0.6f, 4, IsWall ? new Color(0.45f, 0.32f, 0.2f) : new Color(0.55f, 0.5f, 0.45f));
            Battle.Sound(IsWall ? "wall_hit" : "sword_clash", 0.35f, 0.12f);
        }
        if (!IsWall && fire == null && Hp < MaxHp * 0.5f && Hp > 0)
            fire = Fx.Fire(Center - Battle.Flat(Battle.Cam.transform.forward).normalized * Size * 0.45f + Vector3.up * Height * 0.6f, Mathf.Clamp(Size / 4f, 0.6f, 1.2f));
        if (Hp <= 0)
        {
            Hp = 0;
            Collapse();
        }
    }

    void Update()
    {
        var t0 = Battle.Now;
        Tick();
        Battle.Spent("structures", t0);
    }

    void Tick()
    {
        if (Picture != null)
        {
            block ??= new MaterialPropertyBlock();
            if (flash > 0)
            {
                flash -= Time.deltaTime;
                var k = 1 + Mathf.Clamp01(flash / 0.12f) * 0.45f;
                block.SetColor("_BaseColor", new Color(k, k * 0.95f, k * 0.9f, 1));
                Picture.SetPropertyBlock(block);
            }
            else if (Alive)
            {
                Picture.SetPropertyBlock(null);
            }
        }
        if (IsTower && Alive && Battle.Started && !Battle.Over) Shoot();
    }

    // ------------------------------------------------------------------ an archer tower fights back

    const float TowerRange = 13f;

    void Shoot()
    {
        towerCooldown -= Time.deltaTime;
        if (towerCooldown > 0) return;
        Unit best = null;
        var bd = TowerRange;
        foreach (var u in Battle.Units)
        {
            if (u == null || !u.Alive) continue;
            var d = Vector3.Distance(Battle.Flat(u.transform.position), Center);
            if (d < bd) { bd = d; best = u; }
        }
        if (best == null) { towerCooldown = 0.2f; return; }
        towerCooldown = 0.95f;
        Arrow.Shoot(FirePoint, best, 46f, 26f);
        Battle.Sound("arrows", 0.25f, 0.3f);
    }

    // ------------------------------------------------------------------ falling down

    void Collapse()
    {
        Battle.Sound("collapse", IsWall ? 0.5f : 0.9f, 0.05f);
        if (IsWall)
        {
            if (Intact != null) Intact.SetActive(false);
            if (Ruin != null) Ruin.SetActive(true);
            Fx.Dust(Center, 0.7f, 14);
            Fx.Debris(Center + Vector3.up, 1f, 16, new Color(0.42f, 0.3f, 0.18f));
            return;
        }
        CameraRig.Shake(0.25f + Size * 0.04f);
        if (Occluder != null) Occluder.SetActive(false);
        Fx.Dust(Center, Mathf.Clamp(Size / 3f, 0.8f, 1.6f), 46);
        Fx.Debris(Center + Vector3.up * 1.5f, Mathf.Clamp(Size / 3f, 0.8f, 1.5f), 40, new Color(0.5f, 0.42f, 0.34f));
        if (fire != null) fire.transform.position = Center + Vector3.up * 0.5f;
        else fire = Fx.Fire(Center + Vector3.up * 0.4f, Mathf.Clamp(Size / 4f, 0.6f, 1.1f));
        StartCoroutine(SinkPicture());
        Rubble.Build(this);
    }

    IEnumerator SinkPicture()
    {
        if (Picture == null) yield break;
        var t0 = Picture.transform.position;
        var down = -Battle.Cam.transform.up;
        block ??= new MaterialPropertyBlock();
        for (float t = 0; t < 0.9f; t += Time.deltaTime)
        {
            var k = t / 0.9f;
            Picture.transform.position = t0 + down * (k * k * Height * 0.5f) + Random.insideUnitSphere * 0.08f * (1 - k);
            block.SetColor("_BaseColor", new Color(1 - 0.4f * k, 1 - 0.45f * k, 1 - 0.5f * k, 1 - k));
            Picture.SetPropertyBlock(block);
            yield return null;
        }
        Picture.gameObject.SetActive(false);
    }
}

/// <summary>Heaps of stones, beams and a scorched patch where a building stood.</summary>
public static class Rubble
{
    public static void Build(Structure s)
    {
        var root = new GameObject("ruin " + s.Key);
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var stones = new List<CombineInstance>();
        var beams = new List<CombineInstance>();
        var w = s.Max.x - s.Min.x;
        var d = s.Max.y - s.Min.y;
        var n = Mathf.RoundToInt(w * d * 2.4f);
        for (var i = 0; i < n; i++)
        {
            var u = Random.value;
            var v = Random.value;
            var p = new Vector3(Mathf.Lerp(s.Min.x + 0.3f, s.Max.x - 0.3f, u), 0, Mathf.Lerp(s.Min.y + 0.3f, s.Max.y - 0.3f, v));
            // a mound: higher in the middle
            var mid = 1 - Mathf.Max(Mathf.Abs(u - 0.5f), Mathf.Abs(v - 0.5f)) * 2;
            var size = Random.Range(0.35f, 0.95f) * (0.7f + mid * 0.6f);
            p.y = size * 0.3f + mid * Random.Range(0.1f, 0.7f);
            stones.Add(new CombineInstance
            {
                mesh = cube,
                transform = Matrix4x4.TRS(p, Random.rotation, new Vector3(size, size * Random.Range(0.5f, 0.9f), size * Random.Range(0.7f, 1.2f))),
            });
        }
        for (var i = 0; i < n / 10 + 2; i++)
        {
            var p = new Vector3(Random.Range(s.Min.x + 0.5f, s.Max.x - 0.5f), Random.Range(0.2f, 0.6f), Random.Range(s.Min.y + 0.5f, s.Max.y - 0.5f));
            var rot = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-15f, 15f));
            beams.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(p, rot, new Vector3(0.22f, 0.22f, Random.Range(1.6f, 3f))) });
        }
        Part(root, "stones", stones, "Stone");
        Part(root, "beams", beams, "Wood");

        // the scorched ground
        var scorch = Battle.Shape(PrimitiveType.Quad);
        scorch.transform.SetParent(root.transform, false);
        scorch.transform.position = s.Center + Vector3.up * 0.03f;
        scorch.transform.rotation = Quaternion.Euler(90, Random.Range(0, 360f), 0);
        scorch.transform.localScale = new Vector3(w * 1.5f, d * 1.5f, 1);
        var sr = scorch.GetComponent<MeshRenderer>();
        sr.sharedMaterial = Battle.Mat("Scorch");
        sr.shadowCastingMode = ShadowCastingMode.Off;
        root.AddComponent<RiseUp>();
    }

    static void Part(GameObject root, string name, List<CombineInstance> parts, string material)
    {
        var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        mesh.CombineMeshes(parts.ToArray(), true, true);
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = Battle.Mat(material);
    }
}

/// <summary>The ruin grows out of the dust as the building sinks.</summary>
public class RiseUp : MonoBehaviour
{
    float t;

    void Start() => transform.localScale = new Vector3(1, 0.05f, 1);

    void Update()
    {
        t += Time.deltaTime;
        var k = Mathf.Clamp01((t - 0.25f) / 0.7f);
        transform.localScale = new Vector3(1, Mathf.Lerp(0.05f, 1f, 1 - (1 - k) * (1 - k)), 1);
        if (k >= 1) enabled = false;
    }
}
