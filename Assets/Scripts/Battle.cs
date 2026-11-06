using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Shared numbers, lists and helpers of the battle test.</summary>
public static class Battle
{
    /// <summary>One square of the town grid, in metres.</summary>
    public const float Tile = 2f;
    public const int TownSize = 14;
    public static float Half => TownSize / 2f * Tile;
    public const float DeployMargin = 1.8f;
    public const float DeployLimit = 26f;

    public static readonly List<Structure> Structures = new();
    public static readonly List<Structure> Walls = new();
    public static readonly List<Unit> Units = new();
    public static readonly List<MeshRenderer> Pictures = new();
    public static Camera Cam;
    public static bool Started, Over;
    public static int Lost;

    /// <summary>Time spent in each part of the game logic, for the self test.</summary>
    public static readonly Dictionary<string, double> Cost = new();
    public static readonly System.Diagnostics.Stopwatch Watch = System.Diagnostics.Stopwatch.StartNew();
    public static void Spent(string what, double since) => Cost[what] = (Cost.TryGetValue(what, out var v) ? v : 0) + Watch.Elapsed.TotalMilliseconds - since;
    public static double Now => Watch.Elapsed.TotalMilliseconds;

    public static void Reset()
    {
        Structures.Clear();
        Walls.Clear();
        Units.Clear();
        Pictures.Clear();
        Started = Over = false;
        Lost = 0;
        Clips.Clear();
        LastPlayed.Clear();
        _sfx = null;
        _mats.Clear();
        Fx.Reset();
    }

    /// <summary>World point of a town grid position (x, y in squares; 0,0 is the town's corner nearest the camera).</summary>
    public static Vector3 Grid(float x, float y) => new((x - TownSize / 2f) * Tile, 0f, (y - TownSize / 2f) * Tile);

    public static bool InsideTown(Vector3 p) => Mathf.Abs(p.x) < Half && Mathf.Abs(p.z) < Half;

    /// <summary>Soldiers may land only outside the wall, with a little room.</summary>
    public static bool CanDeploy(Vector3 p)
    {
        var m = Half + DeployMargin;
        var lim = Half + DeployLimit;
        return (Mathf.Abs(p.x) > m || Mathf.Abs(p.z) > m) && Mathf.Abs(p.x) < lim && Mathf.Abs(p.z) < lim;
    }

    public static IEnumerable<Structure> Buildings => Structures.Where(s => s != null && s.Alive && !s.IsWall);

    public static float Destruction
    {
        get
        {
            var counted = Structures.Where(s => s != null && !s.IsWall).ToList();
            if (counted.Count == 0) return 0;
            return counted.Sum(s => s.Alive ? 0 : s.Weight) / counted.Sum(s => s.Weight);
        }
    }

    public static int Stars
    {
        get
        {
            var d = Destruction;
            var hall = Structures.FirstOrDefault(s => s != null && s.Key == "town_hall");
            return (d >= 0.5f ? 1 : 0) + (hall != null && !hall.Alive ? 1 : 0) + (d >= 0.999f ? 1 : 0);
        }
    }

    // ------------------------------------------------------------------ walls

    /// <summary>Where the straight walk from [a] to [b] crosses the town's edge, if it does.</summary>
    public static bool Crossing(Vector3 a, Vector3 b, out Vector3 q)
    {
        q = default;
        var ia = InsideTown(a);
        if (ia == InsideTown(b)) return false;
        float lo = 0, hi = 1;
        for (var i = 0; i < 14; i++)
        {
            var mid = (lo + hi) / 2;
            if (InsideTown(Vector3.Lerp(a, b, mid)) == ia) lo = mid; else hi = mid;
        }
        q = Vector3.Lerp(a, b, (lo + hi) / 2);
        return true;
    }

    public static Structure WallAt(Vector3 q)
    {
        Structure best = null;
        var bd = float.MaxValue;
        foreach (var w in Walls)
        {
            var d = (w.Center - q).sqrMagnitude;
            if (d < bd) { bd = d; best = w; }
        }
        return best;
    }

    // ------------------------------------------------------------------ painted pictures standing in the 3D world

    /// <summary>
    /// A painted picture (building, tree) standing at a ground point, [fitTiles] squares wide at its base. The pictures
    /// were painted from this very camera angle, so a quad facing the camera shows them exactly as painted. The quad
    /// slides back along the view (it does not move on screen) to the depth of [depthAt], so soldiers in front of the
    /// building pass in front of it and those behind are hidden.
    /// </summary>
    public static MeshRenderer Picture(string name, Vector3 anchor, float fitTiles, Vector3 depthAt, Transform parent, bool flip = false)
    {
        var tex = Resources.Load<Texture2D>("Buildings/" + name);
        var meta = Resources.Load<TextAsset>("Buildings/" + name);
        if (tex == null) { Debug.LogWarning("missing picture " + name); return null; }
        float ax = 0.5f, ay = 0.95f, bw = tex.width;
        if (meta != null)
        {
            var m = JsonUtility.FromJson<SpriteMeta>(meta.text);
            ax = m.ax;
            ay = m.ay;
            if (m.bw > 0) bw = m.bw;
        }
        // the footprint diamond of n squares is n * tile * sqrt2 wide on screen
        var w = tex.width * fitTiles * Tile * Mathf.Sqrt(2) / bw;
        var h = w * tex.height / tex.width;
        if (flip) ax = 1 - ax;
        // the anchor sits ay of the way down the picture: (1 - ay) of it hangs below the ground point
        float x0 = -ax * w, x1 = (1 - ax) * w, y0 = -(1 - ay) * h, y1 = ay * h;
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(x0, y0, 0), new Vector3(x1, y0, 0), new Vector3(x0, y1, 0), new Vector3(x1, y1, 0) },
            uv = flip
                ? new[] { new Vector2(1, 0), new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 1) }
                : new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) },
            normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back },
            triangles = new[] { 0, 2, 1, 2, 3, 1 },
        };
        mesh.RecalculateBounds();
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var mat = new Material(Mat("SpriteBase"));
        mat.SetTexture("_BaseMap", tex);
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        var view = Cam.transform.forward;
        var depth = Vector3.Dot(depthAt - anchor, view);
        go.transform.SetPositionAndRotation(anchor + view * depth, Cam.transform.rotation);
        Pictures.Add(mr);
        return mr;
    }

    /// <summary>Pictures overlap like a painter's: the farthest first. Done once the town is laid out.</summary>
    public static void SortPictures()
    {
        var view = Cam.transform.forward;
        var order = Pictures.Where(p => p != null).OrderByDescending(p => Vector3.Dot(p.transform.position, view)).ToList();
        for (var i = 0; i < order.Count; i++) order[i].sharedMaterial.renderQueue = 1400 + Mathf.Min(i, 290);
    }

    /// <summary>An invisible block that hides whatever stands behind it (a painted building or tree has no depth of its own).</summary>
    public static GameObject Occluder(Vector3 center, Vector3 size, Transform parent)
    {
        var go = Battle.Shape(PrimitiveType.Cube);
        go.name = "occluder";
        go.transform.SetParent(parent, false);
        go.transform.position = center + Vector3.up * size.y / 2;
        go.transform.localScale = size;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = Mat("Occluder");
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go;
    }

    [System.Serializable]
    class SpriteMeta
    {
        public float ax = 0.5f, ay = 0.95f, bw;
    }

    // ------------------------------------------------------------------ shared materials (made in the editor so the build keeps their shaders)

    static readonly Dictionary<string, Material> _mats = new();

    public static Material Mat(string name)
    {
        if (!_mats.TryGetValue(name, out var m) || m == null)
        {
            m = Resources.Load<Material>("Materials/" + name);
            if (m == null) Debug.LogWarning("missing material " + name);
            _mats[name] = m;
        }
        return m;
    }

    // ------------------------------------------------------------------ sounds

    static readonly Dictionary<string, AudioClip> Clips = new();
    static readonly Dictionary<string, float> LastPlayed = new();
    static AudioSource _sfx;

    public static AudioClip Clip(string name)
    {
        if (!Clips.TryGetValue(name, out var clip))
        {
            clip = Resources.Load<AudioClip>("Audio/" + name);
            Clips[name] = clip;
        }
        return clip;
    }

    public static void Sound(string name, float volume = 0.8f, float minGap = 0.08f)
    {
        if (_sfx == null)
        {
            _sfx = new GameObject("sfx").AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
        }
        if (LastPlayed.TryGetValue(name, out var t) && Time.time - t < minGap) return;
        LastPlayed[name] = Time.time;
        var clip = Clip(name);
        if (clip != null) _sfx.PlayOneShot(clip, volume);
    }

    // ------------------------------------------------------------------ small helpers

    public static float Rand(float a, float b) => Random.Range(a, b);

    public static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            var r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }

    public static Vector3 Flat(Vector3 v) => new(v.x, 0, v.z);

    /// <summary>A plain quad or cube with a renderer and no collider (colliders are not in the build).</summary>
    public static GameObject Shape(PrimitiveType type)
    {
        var name = type == PrimitiveType.Cube ? "Cube" : "Quad";
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>(name + ".fbx");
        go.AddComponent<MeshRenderer>();
        return go;
    }
}
