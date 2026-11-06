using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Everything drawn over the battle: the troop cards, the clock, destruction and stars, health bars, the result.</summary>
public class BattleUI : MonoBehaviour
{
    public class Card
    {
        public UnitKind Kind;
        public string Name;
        public int Count;
        public bool AbilityReady, AbilityUsed;
        public RectTransform Root;
        public Image Frame, Glow;
        public RawImage Portrait;
        public Text CountText, NameText;
    }

    public static readonly Color Gold = new(0.97f, 0.8f, 0.4f);
    public static readonly Color Cream = new(1f, 0.96f, 0.88f);
    public static readonly Color Panel = new(0.07f, 0.06f, 0.05f, 0.78f);

    public readonly List<Card> Cards = new();
    public int Selected = -1;
    public System.Action OnRetry, OnEnd;

    Font baloo, cinzel, nunito;
    Sprite rounded, circle, star;
    RectTransform safe, bars;
    Text timer, destruction, title, hint, enemy;
    Image[] stars = new Image[3];
    readonly List<(Image bg, Image fill)> barPool = new();
    GameObject endButton;
    float hintUntil;

    // ------------------------------------------------------------------ building the screen

    public void Build(List<(UnitKind kind, string name, int count, Texture portrait)> troops)
    {
        baloo = Resources.Load<Font>("Fonts/Baloo2-ExtraBold");
        cinzel = Resources.Load<Font>("Fonts/Cinzel-Bold");
        nunito = Resources.Load<Font>("Fonts/Nunito-ExtraBold");
        rounded = Shapes.Rounded(64, 18);
        circle = Shapes.Circle(64);
        star = Shapes.Star(128);

        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        es.transform.SetParent(transform);
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1;

        bars = Rect("bars", canvasGo.transform);
        Stretch(bars);
        safe = Rect("safe", canvasGo.transform);
        Stretch(safe);
        FitSafeArea();

        // the enemy town, top left
        var name = Rect("enemy", safe);
        Place(name, new Vector2(0, 1), new Vector2(36, -28), new Vector2(620, 120));
        enemy = Label(name, "Thorgard", cinzel, 50, Cream, TextAnchor.UpperLeft);
        var sub = Label(name, "Germanic town, level 6", baloo, 30, new Color(1, 1, 1, 0.75f), TextAnchor.LowerLeft);
        _ = sub;

        // the clock, top middle
        var clock = Img(safe, "clock", rounded, Panel);
        Place(clock.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(230, 104));
        var cl = Label(clock.rectTransform, "Time left", baloo, 24, new Color(1, 1, 1, 0.7f), TextAnchor.UpperCenter);
        cl.rectTransform.offsetMax = new Vector2(0, -6);
        timer = Label(clock.rectTransform, "3:00", nunito, 50, Cream, TextAnchor.LowerCenter);
        timer.rectTransform.offsetMin = new Vector2(0, 8);

        // destruction and stars, top right
        var right = Img(safe, "score", rounded, Panel);
        Place(right.rectTransform, new Vector2(1, 1), new Vector2(-30, -24), new Vector2(330, 186));
        var dl = Label(right.rectTransform, "Destruction", baloo, 24, new Color(1, 1, 1, 0.7f), TextAnchor.UpperCenter);
        dl.rectTransform.offsetMax = new Vector2(0, -8);
        destruction = Label(right.rectTransform, "0%", nunito, 54, Cream, TextAnchor.UpperCenter);
        destruction.rectTransform.offsetMax = new Vector2(0, -38);
        for (var i = 0; i < 3; i++)
        {
            stars[i] = Img(right.rectTransform, "star", star, new Color(0, 0, 0, 0.45f));
            Place(stars[i].rectTransform, new Vector2(0.5f, 0), new Vector2((i - 1) * 62, 14), new Vector2(52, 52));
            stars[i].rectTransform.pivot = new Vector2(0.5f, 0);
        }

        // the troop cards, bottom left
        for (var i = 0; i < troops.Count; i++)
        {
            var (kind, cardName, count, portrait) = troops[i];
            var c = new Card { Kind = kind, Name = cardName, Count = count };
            c.Root = Rect("card " + cardName, safe);
            Place(c.Root, new Vector2(0, 0), new Vector2(40 + i * 200, 28), new Vector2(180, 226));
            c.Glow = Img(c.Root, "glow", rounded, new Color(Gold.r, Gold.g, Gold.b, 0f));
            Stretch(c.Glow.rectTransform, -8);
            c.Frame = Img(c.Root, "frame", rounded, new Color(0.16f, 0.13f, 0.1f, 0.94f));
            Stretch(c.Frame.rectTransform);
            var art = new GameObject("portrait", typeof(RectTransform), typeof(RawImage));
            art.transform.SetParent(c.Frame.transform, false);
            c.Portrait = art.GetComponent<RawImage>();
            c.Portrait.texture = portrait;
            c.Portrait.raycastTarget = false;
            var ar = c.Portrait.rectTransform;
            Stretch(ar);
            ar.offsetMin = new Vector2(8, 44);
            ar.offsetMax = new Vector2(-8, -8);
            c.NameText = Label(c.Frame.rectTransform, cardName, baloo, 28, Cream, TextAnchor.LowerCenter);
            c.NameText.rectTransform.offsetMin = new Vector2(0, 6);
            var badge = Img(c.Frame.rectTransform, "badge", circle, Gold);
            Place(badge.rectTransform, new Vector2(0, 1), new Vector2(-10, 10), new Vector2(68, 68));
            c.CountText = Label(badge.rectTransform, "x" + count, nunito, 30, new Color(0.18f, 0.1f, 0.03f), TextAnchor.MiddleCenter);
            var button = c.Frame.gameObject.AddComponent<Button>();
            var index = i;
            button.onClick.AddListener(() => Tap(index));
            button.transition = Selectable.Transition.None;
            Cards.Add(c);
        }

        // end the battle early, bottom right
        var end = Img(safe, "end", rounded, new Color(0.62f, 0.13f, 0.1f, 0.92f));
        Place(end.rectTransform, new Vector2(1, 0), new Vector2(-36, 34), new Vector2(230, 86));
        Label(end.rectTransform, "End battle", baloo, 32, Cream, TextAnchor.MiddleCenter);
        end.gameObject.AddComponent<Button>().onClick.AddListener(() => OnEnd?.Invoke());
        endButton = end.gameObject;

        title = Label(safe, "", cinzel, 96, Cream, TextAnchor.MiddleCenter);
        title.rectTransform.offsetMin = new Vector2(0, 260);
        title.color = new Color(1, 1, 1, 0);
        var sh = title.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0, 0, 0, 0.7f);
        sh.effectDistance = new Vector2(3, -4);
        hint = Label(safe, "", baloo, 38, Cream, TextAnchor.MiddleCenter);
        hint.rectTransform.offsetMin = new Vector2(0, -360);
        hint.color = new Color(1, 1, 1, 0);
        var hs = hint.gameObject.AddComponent<Outline>();
        hs.effectColor = new Color(0, 0, 0, 0.6f);
        hs.effectDistance = new Vector2(2, -2);
        Refresh();
    }

    void FitSafeArea()
    {
        var s = Screen.safeArea;
        safe.anchorMin = new Vector2(s.xMin / Screen.width, s.yMin / Screen.height);
        safe.anchorMax = new Vector2(s.xMax / Screen.width, s.yMax / Screen.height);
        safe.offsetMin = safe.offsetMax = Vector2.zero;
    }

    // ------------------------------------------------------------------ the cards

    void Tap(int index)
    {
        var c = Cards[index];
        if (c.AbilityReady)
        {
            OnAbility?.Invoke(c);
            return;
        }
        if (c.Count <= 0) return;
        Selected = Selected == index ? -1 : index;
        Battle.Sound("sword_clash", 0.15f, 0.05f);
        Refresh();
    }

    public System.Action<Card> OnAbility;

    public void Refresh()
    {
        for (var i = 0; i < Cards.Count; i++)
        {
            var c = Cards[i];
            var on = i == Selected;
            var empty = c.Count <= 0 && !c.AbilityReady;
            c.Root.localScale = Vector3.one * (on ? 1.06f : 1f);
            c.Root.anchoredPosition = new Vector2(c.Root.anchoredPosition.x, on ? 46 : 28);
            c.Glow.color = new Color(Gold.r, Gold.g, Gold.b, on || c.AbilityReady ? 0.95f : 0f);
            c.Frame.color = empty ? new Color(0.12f, 0.12f, 0.12f, 0.85f) : new Color(0.16f, 0.13f, 0.1f, 0.94f);
            c.Portrait.color = empty ? new Color(0.35f, 0.35f, 0.35f, 1) : Color.white;
            c.CountText.text = c.AbilityReady ? "!" : c.AbilityUsed ? "" : "x" + c.Count;
            c.CountText.transform.parent.gameObject.SetActive(!c.AbilityUsed);
            c.NameText.text = c.AbilityReady ? "Rally" : c.Name;
        }
    }

    // ------------------------------------------------------------------ every frame

    public void Tick(float timeLeft)
    {
        var t = Mathf.CeilToInt(Mathf.Max(0, timeLeft));
        timer.text = $"{t / 60}:{t % 60:00}";
        timer.color = timeLeft < 30 && Battle.Started ? new Color(1f, 0.45f, 0.35f) : Cream;
        var d = Mathf.FloorToInt(Battle.Destruction * 100);
        destruction.text = d + "%";
        var s = Battle.Stars;
        for (var i = 0; i < 3; i++)
        {
            var lit = i < s;
            var want = lit ? Gold : new Color(0, 0, 0, 0.45f);
            if (stars[i].color != want)
            {
                if (lit) StartCoroutine(Pop(stars[i].rectTransform));
                stars[i].color = want;
            }
        }
        if (hint.color.a > 0 && Time.time > hintUntil) hint.color = new Color(1, 1, 1, Mathf.Max(0, hint.color.a - Time.deltaTime * 2));
        Bars();
    }

    void Bars()
    {
        var n = 0;
        var cam = Battle.Cam;
        var scale = bars.rect.height / Screen.height;
        foreach (var s in Battle.Structures)
        {
            if (s == null || !s.Alive || s.Hp >= s.MaxHp || s.IsWall) continue;
            Bar(n++, cam.WorldToScreenPoint(s.Center + Vector3.up * (s.Height + 0.6f)), 120, s.Hp / s.MaxHp, scale, new Color(0.45f, 0.85f, 0.3f));
        }
        foreach (var s in Battle.Walls)
        {
            if (s == null || !s.Alive || s.Hp >= s.MaxHp) continue;
            Bar(n++, cam.WorldToScreenPoint(s.Center + Vector3.up * 3.2f), 60, s.Hp / s.MaxHp, scale, new Color(0.45f, 0.85f, 0.3f));
        }
        foreach (var u in Battle.Units)
        {
            if (u == null || !u.Alive || (u.Hp >= u.MaxHp && !u.IsHero)) continue;
            if (!u.IsHero && u.Hp > u.MaxHp * 0.7f) continue;
            Bar(n++, cam.WorldToScreenPoint(u.transform.position + Vector3.up * 2.1f * u.Scale), u.IsHero ? 90 : 40, u.Hp / u.MaxHp, scale,
                u.IsHero ? Gold : new Color(0.95f, 0.85f, 0.3f));
        }
        for (var i = n; i < barPool.Count; i++) barPool[i].bg.gameObject.SetActive(false);
    }

    void Bar(int i, Vector3 screen, float width, float k, float scale, Color color)
    {
        while (barPool.Count <= i)
        {
            var bg = Img(bars, "bar", rounded, new Color(0, 0, 0, 0.7f));
            bg.rectTransform.anchorMin = bg.rectTransform.anchorMax = Vector2.zero;
            var fill = Img(bg.rectTransform, "fill", rounded, Color.green);
            fill.rectTransform.anchorMin = new Vector2(0, 0);
            fill.rectTransform.anchorMax = new Vector2(1, 1);
            fill.rectTransform.pivot = new Vector2(0, 0.5f);
            fill.raycastTarget = bg.raycastTarget = false;
            barPool.Add((bg, fill));
        }
        var (b, f) = barPool[i];
        b.gameObject.SetActive(screen.z > 0);
        b.rectTransform.sizeDelta = new Vector2(width, width < 50 ? 10 : 14);
        b.rectTransform.anchoredPosition = new Vector2(screen.x, screen.y) * scale;
        f.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(k), 1);
        f.rectTransform.offsetMin = new Vector2(2, 2);
        f.rectTransform.offsetMax = new Vector2(-2, -2);
        f.color = k > 0.35f ? color : new Color(0.95f, 0.3f, 0.2f);
    }

    // ------------------------------------------------------------------ messages

    public void Hint(string text, float seconds = 3f)
    {
        hint.text = text;
        hint.color = Color.white;
        hintUntil = Time.time + seconds;
    }

    public IEnumerator Title(string text, float hold)
    {
        title.text = text;
        for (float t = 0; t < 0.6f; t += Time.deltaTime)
        {
            title.color = new Color(1, 0.95f, 0.85f, t / 0.6f);
            title.rectTransform.localScale = Vector3.one * (1.15f - 0.15f * t / 0.6f);
            yield return null;
        }
        title.color = new Color(1, 0.95f, 0.85f, 1);
        yield return new WaitForSeconds(hold);
        for (float t = 0; t < 0.6f; t += Time.deltaTime)
        {
            title.color = new Color(1, 0.95f, 0.85f, 1 - t / 0.6f);
            yield return null;
        }
        title.color = new Color(1, 1, 1, 0);
    }

    public void ShowCards(bool show)
    {
        foreach (var c in Cards) c.Root.gameObject.SetActive(show);
        endButton.SetActive(show);
    }

    // ------------------------------------------------------------------ the end

    public void Result(bool victory, int starCount, float destructionShare, int lost)
    {
        ShowCards(false);
        var dim = Img(safe.parent, "dim", rounded, new Color(0, 0, 0, 0));
        Stretch(dim.rectTransform, -40);
        StartCoroutine(FadeTo(dim, 0.55f, 0.6f));
        var border = Img(safe.parent, "border", rounded, new Color(Gold.r, Gold.g, Gold.b, 0.9f));
        Place(border.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(968, 628));
        border.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        var panel = Img(safe.parent, "result", rounded, new Color(0.09f, 0.075f, 0.06f, 0.97f));
        Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960, 620));
        panel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        var head = Label(panel.rectTransform, victory ? "Victory" : "Defeat", cinzel, 104, victory ? Gold : new Color(0.9f, 0.45f, 0.4f), TextAnchor.UpperCenter);
        head.rectTransform.offsetMax = new Vector2(0, -26);
        var big = new Image[3];
        for (var i = 0; i < 3; i++)
        {
            big[i] = Img(panel.rectTransform, "star", star, new Color(0, 0, 0, 0.5f));
            Place(big[i].rectTransform, new Vector2(0.5f, 1), new Vector2((i - 1) * 170, -170 - (i == 1 ? 18 : 0)), new Vector2(i == 1 ? 170 : 140, i == 1 ? 170 : 140));
            big[i].rectTransform.pivot = new Vector2(0.5f, 1);
        }
        var line = Label(panel.rectTransform, $"Destruction {Mathf.FloorToInt(destructionShare * 100)}%     Soldiers lost {lost}", baloo, 38, Cream, TextAnchor.MiddleCenter);
        line.rectTransform.offsetMin = new Vector2(0, -60);
        var again = Img(panel.rectTransform, "again", rounded, new Color(0.18f, 0.5f, 0.2f, 1));
        Place(again.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(360, 100));
        again.rectTransform.pivot = new Vector2(0.5f, 0);
        Label(again.rectTransform, "Fight again", baloo, 40, Cream, TextAnchor.MiddleCenter);
        again.gameObject.AddComponent<Button>().onClick.AddListener(() => OnRetry?.Invoke());
        StartCoroutine(StarsIn(big, starCount));
    }

    IEnumerator StarsIn(Image[] big, int count)
    {
        yield return new WaitForSeconds(0.8f);
        for (var i = 0; i < count; i++)
        {
            big[i].color = Gold;
            Battle.Sound("sword_clash", 0.6f, 0.01f);
            yield return Pop(big[i].rectTransform);
            yield return new WaitForSeconds(0.15f);
        }
    }

    IEnumerator Pop(RectTransform r)
    {
        for (float t = 0; t < 0.35f; t += Time.deltaTime)
        {
            var k = t / 0.35f;
            r.localScale = Vector3.one * (1 + Mathf.Sin(k * Mathf.PI) * 0.45f);
            yield return null;
        }
        r.localScale = Vector3.one;
    }

    IEnumerator FadeTo(Image img, float alpha, float time)
    {
        var a0 = img.color.a;
        for (float t = 0; t < time; t += Time.deltaTime)
        {
            var c = img.color;
            c.a = Mathf.Lerp(a0, alpha, t / time);
            img.color = c;
            yield return null;
        }
    }

    // ------------------------------------------------------------------ small builders

    static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform r, float pad = 0)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(pad, pad);
        r.offsetMax = new Vector2(-pad, -pad);
    }

    static void Place(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = anchor;
        r.anchoredPosition = pos;
        r.sizeDelta = size;
    }

    static Image Img(Transform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.type = sprite != null && sprite.border != Vector4.zero ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
        img.color = color;
        return img;
    }

    static Text Label(Transform parent, string text, Font font, int size, Color color, TextAnchor align)
    {
        var go = new GameObject("text", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.text = text;
        t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        Stretch((RectTransform)go.transform);
        return t;
    }

    public static bool OverUi(int pointer)
    {
        if (EventSystem.current == null) return false;
        return pointer < 0 ? EventSystem.current.IsPointerOverGameObject() : EventSystem.current.IsPointerOverGameObject(pointer);
    }
}

/// <summary>Soft edged shapes drawn into small textures for the interface.</summary>
public static class Shapes
{
    public static Sprite Rounded(int size, int radius)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
            var cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
            var d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
            var a = Mathf.Clamp01(radius - d + 0.5f);
            px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
    }

    public static Sprite Circle(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        var r = size / 2f;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
            px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d) * 255));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    public static Sprite Star(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        var pts = new Vector2[10];
        for (var i = 0; i < 10; i++)
        {
            var a = Mathf.PI / 2 + i * Mathf.PI / 5;
            var rr = (i % 2 == 0 ? 0.48f : 0.2f) * size;
            pts[i] = new Vector2(size / 2f + Mathf.Cos(a) * rr, size / 2f + Mathf.Sin(a) * rr - size * 0.02f);
        }
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            // 4x4 samples per pixel for soft edges
            var hits = 0;
            for (var sy = 0; sy < 4; sy++)
            for (var sx = 0; sx < 4; sx++)
                if (Inside(pts, new Vector2(x + (sx + 0.5f) / 4, y + (sy + 0.5f) / 4))) hits++;
            px[y * size + x] = new Color32(255, 255, 255, (byte)(hits * 255 / 16));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    static bool Inside(Vector2[] poly, Vector2 p)
    {
        var inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        }
        return inside;
    }
}
