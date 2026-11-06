using UnityEngine;
using UnityEngine.Rendering;

/// <summary>An arrow in flight: a shaft, a steel head and fletching on a high arc; it sticks where it lands.</summary>
public class Arrow : MonoBehaviour
{
    static GameObject _template;

    Vector3 from, to;
    float flight, t, damage;
    Unit unitTarget;
    Structure buildingTarget;
    float stuck = -1;

    static GameObject Template()
    {
        if (_template != null) return _template;
        _template = new GameObject("arrow");
        _template.SetActive(false);
        _template.transform.localScale = Vector3.one * 1.6f;
        Object.DontDestroyOnLoad(_template);
        Piece(_template.transform, new Vector3(0, 0, -0.05f), new Vector3(0.03f, 0.03f, 0.8f), "Shaft");
        Piece(_template.transform, new Vector3(0, 0, 0.38f), new Vector3(0.06f, 0.025f, 0.12f), "Steel");
        Piece(_template.transform, new Vector3(0, 0.03f, -0.4f), new Vector3(0.008f, 0.06f, 0.16f), "Feather");
        Piece(_template.transform, new Vector3(0.03f, 0, -0.4f), new Vector3(0.06f, 0.008f, 0.16f), "Feather");
        return _template;
    }

    static void Piece(Transform parent, Vector3 pos, Vector3 scale, string material)
    {
        var go = new GameObject(material);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = Battle.Mat(material);
        r.shadowCastingMode = ShadowCastingMode.On;
    }

    public static void Shoot(Vector3 from, Unit target, float damage, float speed)
    {
        // lead the target a little
        var aim = target.transform.position + Vector3.up * 1.2f * target.Scale + target.Velocity * 0.35f;
        Launch(from, aim, damage, speed).unitTarget = target;
    }

    public static void Shoot(Vector3 from, Structure target, float damage, float speed)
    {
        var p = target.ClosestPoint(from);
        var c = target.Center;
        var aim = Vector3.Lerp(p, c, 0.35f) + Vector3.up * Random.Range(0.8f, target.IsWall ? 1.8f : 3f);
        Launch(from, aim, damage, speed).buildingTarget = target;
    }

    static Arrow Launch(Vector3 from, Vector3 to, float damage, float speed)
    {
        var go = Instantiate(Template());
        go.SetActive(true);
        var a = go.AddComponent<Arrow>();
        a.from = from;
        a.to = to;
        a.damage = damage;
        a.flight = Vector3.Distance(from, to) / speed + 0.12f;
        a.Place(0);
        return a;
    }

    Vector3 At(float k)
    {
        var arc = Vector3.Distance(from, to) * 0.22f;
        return Vector3.Lerp(from, to, k) + Vector3.up * (arc * 4 * k * (1 - k));
    }

    void Place(float k)
    {
        var p = At(k);
        var ahead = At(Mathf.Min(k + 0.02f, 1.02f));
        transform.position = p;
        if ((ahead - p).sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(ahead - p);
    }

    void Update()
    {
        if (stuck >= 0)
        {
            stuck += Time.deltaTime;
            if (stuck > 2.5f) Destroy(gameObject);
            return;
        }
        t += Time.deltaTime;
        var k = t / flight;
        if (k < 1)
        {
            Place(k);
            return;
        }
        Place(1);
        stuck = 0;
        if (unitTarget != null && unitTarget.Alive)
        {
            var d = Vector3.Distance(Battle.Flat(unitTarget.transform.position), Battle.Flat(to));
            if (d < 1.4f)
            {
                unitTarget.Hurt(damage);
                // the arrow stays in the soldier
                transform.SetParent(unitTarget.transform, true);
                transform.position = unitTarget.transform.position + Vector3.up * Random.Range(0.9f, 1.4f) * unitTarget.Scale - transform.forward * 0.25f;
                return;
            }
        }
        if (buildingTarget != null && buildingTarget.Alive)
        {
            buildingTarget.Hit(damage, to, false);
            Fx.Debris(to, 0.4f, 2, new Color(0.5f, 0.42f, 0.3f));
            Destroy(gameObject, 0.15f);
            return;
        }
        // into the ground
        if (transform.position.y < 0.3f) transform.position = new Vector3(transform.position.x, 0.12f, transform.position.z);
    }
}
