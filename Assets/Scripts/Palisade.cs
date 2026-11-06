using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Sharpened log palisade, built as real geometry so it catches the sun, casts shadows and breaks apart.</summary>
public static class Palisade
{
    /// <summary>One square's stretch of wall from [a] to [b]; [ruin] builds what is left after it is broken.</summary>
    public static GameObject Stretch(Transform parent, Vector3 a, Vector3 b, Vector3 normal, System.Random rnd, bool ruin)
    {
        float R() => (float)rnd.NextDouble();
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        var along = (b - a).normalized;
        const int logs = 5;
        for (var i = 0; i < logs; i++)
        {
            var t = (i + 0.5f) / logs;
            var basePos = Vector3.Lerp(a, b, t) + normal * (R() - 0.5f) * 0.08f;
            var radius = 0.17f + R() * 0.04f;
            if (!ruin)
            {
                var h = 3.0f + R() * 0.6f;
                var lean = (along * (R() - 0.5f) * 0.05f + normal * (R() - 0.3f) * 0.05f);
                Log(verts, norms, uvs, tris, basePos, Vector3.up + lean, radius, h, true, R());
            }
            else
            {
                // splintered stumps, and some of the logs lying where they fell
                Log(verts, norms, uvs, tris, basePos, Vector3.up, radius, 0.25f + R() * 0.55f, false, R());
                if (R() < 0.55f)
                {
                    var fall = (normal * (R() < 0.5f ? 1 : -1) + along * (R() - 0.5f) * 0.8f).normalized;
                    var lie = Vector3.Lerp(fall, Vector3.up, 0.06f).normalized;
                    Log(verts, norms, uvs, tris, basePos + fall * 0.5f + Vector3.up * radius, lie, radius, 1.6f + R() * 0.8f, true, R());
                }
            }
        }
        if (!ruin)
        {
            // two rails behind the logs bind them together
            foreach (var y in new[] { 0.85f, 2.2f })
            {
                var mid = (a + b) / 2 - normal * 0.24f + Vector3.up * y;
                Beam(verts, norms, uvs, tris, mid - along * 1.02f, mid + along * 1.02f, 0.09f);
            }
        }
        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        var go = new GameObject(ruin ? "ruin" : "logs");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = Battle.Mat("Wood");
        r.shadowCastingMode = ShadowCastingMode.On;
        r.receiveShadows = true;
        return go;
    }

    /// <summary>A rough log: eight sides, slightly uneven, with a sharpened or a broken top.</summary>
    static void Log(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 basePos, Vector3 axis, float r, float h, bool point, float seed)
    {
        axis = axis.normalized;
        var side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
        var side2 = Vector3.Cross(axis, side);
        const int sides = 8;
        var rings = new[] { 0f, 0.5f, 1f };
        var start = v.Count;
        var u0 = seed;
        for (var ri = 0; ri < rings.Length; ri++)
        {
            var k = rings[ri];
            var y = k * h;
            for (var s = 0; s <= sides; s++)
            {
                var ang = s * Mathf.PI * 2 / sides;
                var wob = 1 + Mathf.Sin(ang * 3 + seed * 20 + k * 4) * 0.06f;
                var dir = side * Mathf.Cos(ang) + side2 * Mathf.Sin(ang);
                v.Add(basePos + axis * y + dir * r * wob);
                n.Add(dir);
                uv.Add(new Vector2(u0 + s / (float)sides, y * 0.6f));
            }
        }
        for (var ri = 0; ri < rings.Length - 1; ri++)
        for (var s = 0; s < sides; s++)
        {
            var i0 = start + ri * (sides + 1) + s;
            var i1 = i0 + sides + 1;
            t.AddRange(new[] { i0, i1, i0 + 1, i0 + 1, i1, i1 + 1 });
        }
        var top = start + (rings.Length - 1) * (sides + 1);
        var tip = basePos + axis * (h + (point ? r * 2.2f : 0.03f));
        for (var s = 0; s < sides; s++)
        {
            var ang = (s + 0.5f) * Mathf.PI * 2 / sides;
            var dir = side * Mathf.Cos(ang) + side2 * Mathf.Sin(ang);
            var c = v.Count;
            v.Add(v[top + s]);
            v.Add(v[top + s + 1]);
            v.Add(tip);
            var fn = (dir + axis * (point ? 0.55f : 3f)).normalized;
            n.Add(fn);
            n.Add(fn);
            n.Add(fn);
            uv.Add(new Vector2(0.1f, 0.95f));
            uv.Add(new Vector2(0.2f, 0.95f));
            uv.Add(new Vector2(0.15f, 0.99f));
            t.AddRange(new[] { c, c + 2, c + 1 });
        }
    }

    static void Beam(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 a, Vector3 b, float r)
    {
        var axis = (b - a).normalized;
        var side = Vector3.Cross(axis, Vector3.up).normalized;
        var side2 = Vector3.Cross(axis, side);
        var len = Vector3.Distance(a, b);
        const int sides = 6;
        var start = v.Count;
        for (var e = 0; e < 2; e++)
        for (var s = 0; s <= sides; s++)
        {
            var ang = s * Mathf.PI * 2 / sides;
            var dir = side * Mathf.Cos(ang) + side2 * Mathf.Sin(ang);
            v.Add((e == 0 ? a : b) + dir * r);
            n.Add(dir);
            uv.Add(new Vector2(s / (float)sides * 0.3f, e * len * 0.6f));
        }
        for (var s = 0; s < sides; s++)
        {
            var i0 = start + s;
            var i1 = i0 + sides + 1;
            t.AddRange(new[] { i0, i0 + 1, i1, i0 + 1, i1 + 1, i1 });
        }
    }
}
