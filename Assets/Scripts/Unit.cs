using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public enum UnitKind { Swordsman, Archer, Hero }

/// <summary>Loads the motion captured characters and their moves.</summary>
public static class UnitKit
{
    public static string Folder(UnitKind k) => k switch
    {
        UnitKind.Archer => "archer",
        UnitKind.Hero => "knight",
        _ => "paladin",
    };

    static readonly Dictionary<string, AnimationClip[]> Clips = new();
    static List<(string bone, Mesh mesh, Material[] mats)> _props;

    public static AnimationClip[] ClipsFor(string folder)
    {
        if (!Clips.TryGetValue(folder, out var list))
        {
            list = Resources.LoadAll<AnimationClip>("Units/" + folder).Where(c => !c.name.StartsWith("__")).ToArray();
            Clips[folder] = list;
        }
        return list;
    }

    public static GameObject Spawn(UnitKind kind)
    {
        var folder = Folder(kind);
        var prefab = Resources.Load<GameObject>($"Units/{folder}/{folder}_idle");
        var go = Object.Instantiate(prefab);
        go.name = kind.ToString();
        var anim = go.GetComponent<Animation>();
        if (anim == null) anim = go.AddComponent<Animation>();
        foreach (var clip in ClipsFor(folder))
            if (anim.GetClip(clip.name) == null) anim.AddClip(clip, clip.name);
        anim.cullingType = AnimationCullingType.AlwaysAnimate;
        anim.playAutomatically = false;
        foreach (AnimationState s in anim)
        {
            var loop = s.name is "idle" or "walk" or "run" or "cheer" or "aim";
            s.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
        }
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            r.updateWhenOffscreen = false;
            r.skinnedMotionVectors = false;
            r.quality = SkinQuality.Bone4;
        }
        if (kind == UnitKind.Hero) BorrowProps(go);
        return go;
    }

    /// <summary>
    /// The hero is the hooded knight; he takes the sword and shield of the armoured knight. Both skeletons share bone
    /// names and axes, so each prop is moved into its hand bone's own space and hung on the same bone of the hero.
    /// </summary>
    static void BorrowProps(GameObject hero)
    {
        if (_props == null)
        {
            _props = new();
            var donor = Resources.Load<GameObject>("Units/paladin/paladin_idle");
            // props that are already rigid: a mesh hanging under a hand bone
            foreach (var mf in donor.GetComponentsInChildren<MeshFilter>(true))
            {
                var hand = mf.transform.parent;
                while (hand != null && !hand.name.Contains("Hand") && !hand.name.Contains("ForeArm")) hand = hand.parent;
                if (hand == null || mf.sharedMesh == null) continue;
                var rel = hand.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var m2 = Object.Instantiate(mf.sharedMesh);
                var vv = m2.vertices;
                for (var i = 0; i < vv.Length; i++) vv[i] = rel.MultiplyPoint3x4(vv[i]);
                m2.vertices = vv;
                m2.RecalculateNormals();
                m2.RecalculateBounds();
                _props.Add((hand.name, m2, mf.GetComponent<MeshRenderer>().sharedMaterials));
            }
            foreach (var smr in donor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                var used = new HashSet<int>();
                foreach (var bw in mesh.boneWeights)
                {
                    if (bw.weight0 > 0.01f) used.Add(bw.boneIndex0);
                    if (bw.weight1 > 0.01f) used.Add(bw.boneIndex1);
                    if (bw.weight2 > 0.01f) used.Add(bw.boneIndex2);
                    if (bw.weight3 > 0.01f) used.Add(bw.boneIndex3);
                    if (used.Count > 1) break;
                }
                if (used.Count != 1) continue;
                var bi = used.First();
                var joint = smr.bones[bi];
                if (!joint.name.Contains("Sword") && !joint.name.Contains("Shield")) continue;
                // the prop's own joint hangs under a hand: carry the prop into that hand's space
                var handT = joint.parent;
                while (handT != null && !handT.name.Contains("Hand") && !handT.name.Contains("ForeArm")) handT = handT.parent;
                if (handT == null) continue;
                var bone = handT.name;
                var bind = handT.worldToLocalMatrix * joint.localToWorldMatrix * mesh.bindposes[bi];
                var v = mesh.vertices;
                var n = mesh.normals;
                var t = mesh.tangents;
                for (var i = 0; i < v.Length; i++)
                {
                    v[i] = bind.MultiplyPoint3x4(v[i]);
                    if (i < n.Length) n[i] = bind.MultiplyVector(n[i]).normalized;
                    if (i < t.Length)
                    {
                        var tv = bind.MultiplyVector(new Vector3(t[i].x, t[i].y, t[i].z)).normalized;
                        t[i] = new Vector4(tv.x, tv.y, tv.z, t[i].w);
                    }
                }
                var baked = new Mesh { name = mesh.name + " prop" };
                baked.vertices = v;
                if (n.Length == v.Length) baked.normals = n;
                if (t.Length == v.Length) baked.tangents = t;
                baked.uv = mesh.uv;
                baked.subMeshCount = mesh.subMeshCount;
                for (var sm = 0; sm < mesh.subMeshCount; sm++) baked.SetTriangles(mesh.GetTriangles(sm), sm);
                baked.RecalculateBounds();
                _props.Add((bone, baked, smr.sharedMaterials));
            }
        }
        foreach (var (bone, mesh, mats) in _props)
        {
            var b = Battle.FindDeep(hero.transform, bone);
            if (b == null) continue;
            var go = new GameObject("prop");
            go.transform.SetParent(b, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
        }
    }
}

/// <summary>A soldier on the field: picks a target, runs to it (through breaches in the wall), fights and falls.</summary>
public class Unit : MonoBehaviour
{
    public UnitKind Kind;
    public float MaxHp, Hp, Damage, Range, Speed, Scale, RunNative;
    public Vector3 Velocity;
    public bool Alive => Hp > 0;
    public bool IsHero => Kind == UnitKind.Hero;
    public float BoostUntil;

    Animation anim;
    Structure target;
    Vector3 waypoint;
    bool hasWaypoint;
    float think;
    string playing;
    // the current attack: which clip, how far through it, which strikes already landed
    string action;
    float actionT, actionLen;
    float[] strikes;
    int struck;
    float deadT = -1;
    float hurtFlash;
    float pause;
    Renderer[] renderers;
    MaterialPropertyBlock block;

    static readonly Dictionary<UnitKind, (float hp, float dmg, float range, float speed, float scale, float run)> Stats = new()
    {
        [UnitKind.Swordsman] = (320, 42, 0.95f, 5.2f, 1.75f, 3.9f),
        [UnitKind.Archer] = (150, 28, 8f, 5.0f, 1.7f, 3.7f),
        [UnitKind.Hero] = (2400, 130, 1.2f, 5.0f, 2.05f, 3.9f),
    };

    public static Unit Create(UnitKind kind, Vector3 at, Vector3 face)
    {
        var go = UnitKit.Spawn(kind);
        var u = go.AddComponent<Unit>();
        var s = Stats[kind];
        u.Kind = kind;
        u.MaxHp = u.Hp = s.hp;
        u.Damage = s.dmg;
        u.Range = s.range;
        u.Speed = s.speed * Random.Range(0.94f, 1.06f);
        u.Scale = s.scale;
        u.RunNative = s.run;
        go.transform.localScale = Vector3.one * s.scale;
        go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(Battle.Flat(face - at).normalized + new Vector3(0.001f, 0, 0)));
        u.anim = go.GetComponent<Animation>();
        u.renderers = go.GetComponentsInChildren<Renderer>();
        u.think = Random.Range(0f, 0.25f);
        Battle.Units.Add(u);
        if (kind == UnitKind.Hero)
        {
            var ring = Battle.Shape(PrimitiveType.Quad);
            ring.transform.SetParent(go.transform, false);
            ring.transform.localPosition = Vector3.up * 0.04f / s.scale;
            ring.transform.localRotation = Quaternion.Euler(90, 0, 0);
            ring.transform.localScale = Vector3.one * 1.6f;
            var rr = ring.GetComponent<MeshRenderer>();
            rr.sharedMaterial = Battle.Mat("HeroRing");
            rr.shadowCastingMode = ShadowCastingMode.Off;
        }
        u.Play("idle", 0f);
        return u;
    }

    void Play(string clip, float fade = 0.15f, float speed = 1f)
    {
        if (anim[clip] == null) return;
        anim[clip].speed = speed;
        if (playing == clip) return;
        playing = clip;
        if (fade <= 0) anim.Play(clip);
        else anim.CrossFade(clip, fade);
    }

    void Restart(string clip, float fade, float speed)
    {
        if (anim[clip] == null) return;
        anim[clip].speed = speed;
        anim[clip].time = 0;
        playing = clip;
        anim.CrossFade(clip, fade);
    }

    float Boost => Time.time < BoostUntil ? 1.6f : 1f;

    void Update()
    {
        var t0 = Battle.Now;
        Tick();
        Battle.Spent("units", t0);
    }

    void Tick()
    {
        var dt = Time.deltaTime;
        if (deadT >= 0)
        {
            deadT += dt;
            if (deadT > 4.5f) transform.position += Vector3.down * dt * 0.35f * Scale;
            if (deadT > 7f)
            {
                Battle.Units.Remove(this);
                Destroy(gameObject);
            }
            return;
        }
        Flash(dt);
        if (Battle.Over)
        {
            Velocity = Vector3.zero;
            return;
        }
        if (pause > 0)
        {
            pause -= dt;
            Velocity = Vector3.zero;
            return;
        }
        if (action != null)
        {
            Act(dt);
            return;
        }
        think -= dt;
        if (think <= 0)
        {
            think = 0.3f + Random.Range(0f, 0.15f);
            Think();
        }
        if (target == null || !target.Alive)
        {
            Velocity = Vector3.zero;
            Play("idle");
            return;
        }
        if (target.DistanceTo(transform.position) <= Range + 0.3f && (!Battle.InsideTown(transform.position) || Battle.InsideTown(target.Center) || target.IsWall || Kind == UnitKind.Archer))
        {
            StartAttack();
            return;
        }
        Move(dt);
    }

    // ------------------------------------------------------------------ choosing where to go

    void Think()
    {
        var pos = transform.position;
        if (target == null || !target.Alive || target.IsWall)
        {
            var wall = target != null && target.IsWall && target.Alive ? target : null;
            Structure best = null;
            var bd = float.MaxValue;
            foreach (var s in Battle.Buildings)
            {
                var d = s.DistanceTo(pos);
                if (d < bd) { bd = d; best = s; }
            }
            // keep hacking at the wall only while there is no way round it
            target = best;
            if (wall != null && best != null && !OpenWay(pos, best)) target = wall;
        }
        hasWaypoint = false;
        if (target == null || target.IsWall) return;
        var goal = target.ClosestPoint(pos);
        if (Kind == UnitKind.Archer && target.DistanceTo(pos) <= Range) return;
        var inside = Battle.InsideTown(pos);
        if (inside == Battle.InsideTown(goal)) return;
        // through the nearest breach, if one is not much of a detour
        Structure gap = null;
        var cost = float.MaxValue;
        var direct = Vector3.Distance(pos, goal);
        foreach (var w in Battle.Walls)
        {
            if (w.Alive) continue;
            var c = Vector3.Distance(pos, w.Center) + Vector3.Distance(w.Center, goal);
            if (c < cost) { cost = c; gap = w; }
        }
        if (gap == null || cost > direct + 16f) return;
        var side = inside ? -1 : 1;
        var off = pos - gap.Center;
        var across = Vector3.Dot(off, gap.Normal);
        var along = (off - gap.Normal * across).magnitude;
        // near the gap: go through it; otherwise first to its mouth
        waypoint = Mathf.Abs(across) < 1.6f && along < Battle.Tile * 0.45f
            ? gap.Center - gap.Normal * side * 1.4f
            : gap.Center + gap.Normal * side * 1.1f;
        hasWaypoint = true;
    }

    bool OpenWay(Vector3 pos, Structure goal)
    {
        if (!Battle.Crossing(pos, goal.ClosestPoint(pos), out _)) return true;
        return Battle.Walls.Any(w => !w.Alive);
    }

    void Move(float dt)
    {
        var pos = transform.position;
        var goal = hasWaypoint ? waypoint : target.ClosestPoint(pos);
        var dir = Battle.Flat(goal - pos);
        if (hasWaypoint && dir.magnitude < 0.4f)
        {
            hasWaypoint = false;
            think = 0;
            return;
        }
        var want = dir.normalized * Speed * (Boost > 1 ? 1.2f : 1f);
        // keep a little room between soldiers
        var push = Vector3.zero;
        foreach (var o in Battle.Units)
        {
            if (o == this || o == null || !o.Alive) continue;
            var d = Battle.Flat(pos - o.transform.position);
            var room = 0.55f * (Scale + o.Scale) * 0.75f;
            var m = d.magnitude;
            if (m < room && m > 1e-4f) push += d / m * (room - m) / room;
        }
        var v = want + push * Speed * 1.4f;
        Velocity = Vector3.Lerp(Velocity, v, 1 - Mathf.Exp(-10 * dt));
        var next = pos + Velocity * dt;
        // the palisade stands until it is broken
        if (Battle.Crossing(pos, next, out var q))
        {
            var wall = Battle.WallAt(q);
            if (wall != null && wall.Alive)
            {
                next = pos;
                if (!(Kind == UnitKind.Archer && target.DistanceTo(pos) <= Range))
                {
                    target = wall;
                    hasWaypoint = false;
                }
            }
        }
        // walk round the buildings in the way
        foreach (var s in Battle.Structures)
        {
            if (s == null || !s.Alive || s.IsWall || s == target) continue;
            next = s.PushOut(next, 0.3f);
        }
        transform.position = next;
        var flat = Battle.Flat(Velocity);
        if (flat.sqrMagnitude > 0.05f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), 1 - Mathf.Exp(-12 * dt));
        var speed = flat.magnitude;
        if (speed > 0.6f) Play("run", 0.15f, Mathf.Clamp(speed / (RunNative * Scale), 0.6f, 1.4f));
        else Play("idle", 0.2f);
    }

    // ------------------------------------------------------------------ fighting

    void StartAttack()
    {
        Velocity = Vector3.zero;
        Face(target.ClosestPoint(transform.position));
        if (Kind == UnitKind.Archer)
        {
            Begin("draw", new float[0], 1.1f);
            return;
        }
        if (IsHero)
        {
            var pick = Random.Range(0, 3);
            if (pick == 0) Begin("attack1", new[] { 0.45f }, 1.1f);
            else if (pick == 1) Begin("attack2", new[] { 0.24f, 0.5f, 0.76f }, 1.15f);
            else Begin("attack3", new[] { 0.55f }, 1.1f);
            return;
        }
        var n = Random.Range(1, 4);
        Begin("attack" + n, new[] { n == 1 ? 0.5f : n == 2 ? 0.48f : 0.42f }, 1.15f);
    }

    void Begin(string clip, float[] hits, float speed)
    {
        if (anim[clip] == null) { pause = 0.5f; return; }
        action = clip;
        actionT = 0;
        actionLen = anim[clip].length;
        strikes = hits;
        struck = 0;
        Restart(clip, 0.12f, speed);
    }

    void Act(float dt)
    {
        var st = anim[action];
        actionT += dt * (st != null ? st.speed : 1f);
        if (target != null && target.Alive) Face(target.ClosestPoint(transform.position), dt);
        while (struck < strikes.Length && actionT >= strikes[struck] * actionLen)
        {
            Strike(strikes.Length);
            struck++;
        }
        if (Kind == UnitKind.Archer && action == "shoot" && struck == 0 && actionT > 0.06f)
        {
            struck = 1;
            if (target != null && target.Alive)
            {
                var hand = transform.position + transform.forward * 0.4f * Scale + Vector3.up * 1.45f * Scale;
                Arrow.Shoot(hand, target, Damage * Boost, 24f);
                Battle.Sound("arrows", 0.18f, 0.25f);
            }
        }
        if (actionT < actionLen) return;
        var was = action;
        action = null;
        if (target == null || !target.Alive || Battle.Over)
        {
            think = 0;
            return;
        }
        if (Kind == UnitKind.Archer)
        {
            if (was == "draw") { Begin("shoot", new float[0], 1f); return; }
            if (target.DistanceTo(transform.position) <= Range + 0.3f) { Begin("draw", new float[0], 1.1f); return; }
            return;
        }
        if (target.DistanceTo(transform.position) <= Range + 0.4f) StartAttack();
    }

    void Strike(int of)
    {
        if (target == null || !target.Alive) return;
        var at = target.ClosestPoint(transform.position);
        var hitPoint = at + Vector3.up * Random.Range(0.6f, 1.4f) + Battle.Flat(transform.position - at).normalized * 0.1f;
        target.Hit(Damage * Boost / of, hitPoint);
        Fx.Sparks(hitPoint, IsHero ? 14 : 7);
        if (IsHero) CameraRig.Shake(0.08f);
    }

    void Face(Vector3 p, float dt = 1f)
    {
        var d = Battle.Flat(p - transform.position);
        if (d.sqrMagnitude < 1e-4f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), dt >= 1 ? 1 : 1 - Mathf.Exp(-10 * dt));
    }

    // ------------------------------------------------------------------ taking hits

    public void Hurt(float damage)
    {
        if (!Alive) return;
        Hp -= damage;
        hurtFlash = 0.15f;
        if (Hp <= 0)
        {
            Hp = 0;
            Die();
            return;
        }
        // a stagger now and then, when not in the middle of a swing
        if (action == null && !IsHero && Random.value < 0.3f && anim["hit"] != null)
        {
            Restart("hit", 0.08f, 1.1f);
            pause = anim["hit"].length * 0.8f;
        }
    }

    void Die()
    {
        action = null;
        Velocity = Vector3.zero;
        deadT = 0;
        Battle.Lost++;
        var death = anim["death2"] != null && Random.value < 0.5f ? "death2" : "death1";
        Restart(death, 0.1f, 1f);
        foreach (var r in renderers)
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
        var ring = transform.Find("Quad");
        if (ring != null) ring.gameObject.SetActive(false);
    }

    void Flash(float dt)
    {
        if (hurtFlash <= 0) return;
        hurtFlash -= dt;
        block ??= new MaterialPropertyBlock();
        foreach (var r in renderers)
        {
            if (hurtFlash > 0)
            {
                block.SetColor("_BaseColor", new Color(1.6f, 0.75f, 0.7f, 1));
                r.SetPropertyBlock(block);
            }
            else
            {
                r.SetPropertyBlock(null);
            }
        }
    }

    // ------------------------------------------------------------------ the end

    public void Cheer()
    {
        if (!Alive) return;
        action = null;
        Velocity = Vector3.zero;
        if (anim["cheer"] != null)
        {
            anim["cheer"].time = Random.Range(0f, 1f);
            Play("cheer", 0.3f, Random.Range(0.9f, 1.1f));
        }
    }

    public void Rally()
    {
        if (!Alive) return;
        action = null;
        Velocity = Vector3.zero;
        if (anim["power"] != null)
        {
            Restart("power", 0.1f, 1.1f);
            pause = anim["power"].length / 1.1f;
        }
        Fx.Aura(transform, 9f);
        Fx.Ring(transform.position, 18f, new Color(1f, 0.82f, 0.35f, 0.9f), 0.9f);
        Battle.Sound("horn_attack", 0.7f);
        foreach (var u in Battle.Units)
        {
            if (u == null || !u.Alive) continue;
            if (Vector3.Distance(u.transform.position, transform.position) > 10f) continue;
            u.Hp = Mathf.Min(u.MaxHp, u.Hp + u.MaxHp * 0.5f);
            u.BoostUntil = Time.time + 10f;
        }
    }
}
