using UnityEngine;

/// <summary>The fixed angle camera: pans and zooms, never turns. Opens the battle with a sweep down onto the town.</summary>
public class CameraRig : MonoBehaviour
{
    public static CameraRig Instance;
    public Camera Cam;
    public Vector3 Focus;
    public float Size = 15f;
    public const float MinSize = 7f, MaxSize = 24f;
    public bool Intro;

    float shake;
    Vector3 introFrom;
    float introFromSize, introT, introLen;

    public static void Shake(float amount)
    {
        if (Instance != null) Instance.shake = Mathf.Max(Instance.shake, amount);
    }

    void Awake() => Instance = this;

    public void PlayIntro(float length)
    {
        Intro = true;
        introT = 0;
        introLen = length;
        // from close over the town hall's roof out to the whole town
        introFrom = new Vector3(1.5f, 0, 1.5f);
        introFromSize = 7.5f;
    }

    public void Pan(Vector2 screenDelta)
    {
        // a drag moves the ground under the finger
        var units = 2 * Cam.orthographicSize / Screen.height;
        var right = Cam.transform.right;
        var fwd = Battle.Flat(Cam.transform.up).normalized;
        var upScale = 1f / Mathf.Sin(30 * Mathf.Deg2Rad);
        Focus -= right * screenDelta.x * units + fwd * screenDelta.y * units * upScale;
        Clamp();
    }

    public void Zoom(float factor, Vector2 around)
    {
        var before = Ground(around);
        Size = Mathf.Clamp(Size * factor, MinSize, MaxSize);
        Apply();
        var after = Ground(around);
        Focus += before - after;
        Clamp();
    }

    void Clamp()
    {
        var lim = Battle.Half + 14f;
        Focus.x = Mathf.Clamp(Focus.x, -lim, lim);
        Focus.z = Mathf.Clamp(Focus.z, -lim, lim);
        Focus.y = 0;
    }

    public Vector3 Ground(Vector2 screen)
    {
        var ray = Cam.ScreenPointToRay(screen);
        var plane = new Plane(Vector3.up, Vector3.zero);
        return plane.Raycast(ray, out var d) ? ray.GetPoint(d) : Focus;
    }

    void LateUpdate()
    {
        var focus = Focus;
        var size = Size;
        if (Intro)
        {
            introT += Time.deltaTime;
            var k = Mathf.Clamp01(introT / introLen);
            var e = k < 0.5f ? 4 * k * k * k : 1 - Mathf.Pow(-2 * k + 2, 3) / 2;
            focus = Vector3.Lerp(introFrom, Focus, e);
            size = Mathf.Lerp(introFromSize, Size, e);
            if (k >= 1) Intro = false;
        }
        Apply(focus, size);
        if (shake > 0)
        {
            shake = Mathf.Max(0, shake - Time.deltaTime * 1.6f);
            var s = shake * shake * 0.6f;
            Cam.transform.position += Cam.transform.right * (Mathf.PerlinNoise(Time.time * 30, 1) - 0.5f) * s + Cam.transform.up * (Mathf.PerlinNoise(1, Time.time * 30) - 0.5f) * s;
        }
    }

    void Apply() => Apply(Focus, Size);

    void Apply(Vector3 focus, float size)
    {
        Cam.orthographicSize = size;
        Cam.transform.rotation = Quaternion.Euler(30, 45, 0);
        Cam.transform.position = focus - Cam.transform.forward * 80f;
    }
}
