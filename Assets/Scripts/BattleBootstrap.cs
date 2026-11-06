using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// The battle test: builds a Germanic town with its palisade and archer towers, the attacker's troop cards, and runs
/// one live battle (deploy, fight, stars, result). Everything is made in code so the scene file stays empty.
/// </summary>
public class BattleBootstrap : MonoBehaviour
{
    const float BattleTime = 180f;

    BattleUI ui;
    CameraRig rig;
    AudioSource music;
    LineRenderer border;
    float borderRed;
    float timeLeft = BattleTime;
    bool ended;
    readonly List<RenderTexture> portraits = new();

    // the finger (or mouse) on the field
    bool pointerDown, panning, deploying;
    Vector2 downPos, lastPos, lastMouse;
    float downTime, nextDeploy, lastMarch;

    void Awake()
    {
        Battle.Reset();
        Application.targetFrameRate = 60;
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        BuildCamera();
        BuildLight();
        BuildPost();
        BuildGround();
        BuildTown();
        BuildWalls();
        BuildNature();
        Battle.SortPictures();
        BuildBorder();
        StartCoroutine(Begin());
        AutoTest.StartIfAsked(gameObject);
    }

    /// <summary>For the self test: sends one soldier of card [card] to a ground point.</summary>
    public void DeployAt(int card, Vector3 p)
    {
        ui.Selected = card;
        Deploy(Battle.Cam.WorldToScreenPoint(p));
    }

    public void RallyNow()
    {
        var card = ui.Cards.FirstOrDefault(c => c.AbilityReady);
        if (card != null) Ability(card);
    }

    // ------------------------------------------------------------------ camera, light, look

    void BuildCamera()
    {
        var go = new GameObject("Camera");
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.nearClipPlane = 0.5f;
        cam.farClipPlane = 160f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.36f, 0.42f, 0.24f);
        cam.allowHDR = true;
        cam.allowMSAA = true;
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.High;
        data.renderShadows = true;
        go.AddComponent<AudioListener>();
        Battle.Cam = cam;
        rig = go.AddComponent<CameraRig>();
        rig.Cam = cam;
        rig.Focus = new Vector3(-2f, 0, -2f);
        rig.Size = 15.5f;
        cam.transform.rotation = Quaternion.Euler(30, 45, 0);
        cam.transform.position = rig.Focus - cam.transform.forward * 50f;
    }

    void BuildLight()
    {
        // the sun stands high at the top left of the screen, as in the painted buildings
        var go = new GameObject("Sun");
        var sun = go.AddComponent<Light>();
        sun.type = LightType.Directional;
        var toSun = new Vector3(-2.2f, 8.5f, 5.0f).normalized;
        go.transform.rotation = Quaternion.LookRotation(-toSun);
        sun.color = new Color(1f, 0.95f, 0.86f);
        sun.intensity = 1.35f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.78f;
        sun.shadowBias = 0.04f;
        sun.shadowNormalBias = 0.3f;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.78f);
        RenderSettings.ambientEquatorColor = new Color(0.5f, 0.5f, 0.46f);
        RenderSettings.ambientGroundColor = new Color(0.32f, 0.29f, 0.24f);
        RenderSettings.ambientIntensity = 1f;
    }

    void BuildPost()
    {
        var go = new GameObject("Look");
        var volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.Neutral);
        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(1.05f);
        bloom.intensity.Override(0.35f);
        bloom.scatter.Override(0.6f);
        var color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(0.05f);
        color.contrast.Override(14f);
        color.saturation.Override(10f);
        var lgg = profile.Add<ShadowsMidtonesHighlights>(true);
        lgg.shadows.Override(new Vector4(0.96f, 0.98f, 1.06f, 0f));
        lgg.highlights.Override(new Vector4(1.04f, 1.0f, 0.95f, 0f));
        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.26f);
        vignette.smoothness.Override(0.45f);
        volume.profile = profile;
    }

    void BuildGround()
    {
        var ground = Battle.Shape(PrimitiveType.Quad);
        ground.name = "Ground";
        ground.transform.rotation = Quaternion.Euler(90, 0, 0);
        ground.transform.localScale = new Vector3(260, 260, 1);
        var r = ground.GetComponent<MeshRenderer>();
        r.sharedMaterial = Battle.Mat("Ground");
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = true;
        Patches();
    }

    /// <summary>Worn earth: along both sides of the palisade, between the buildings and on the army's approach.</summary>
    void Patches()
    {
        var root = new GameObject("Worn ground").transform;
        var rnd = new System.Random(5);
        float R() => (float)rnd.NextDouble();
        var h = Battle.Half;
        void Patch(Vector3 p, float size)
        {
            var q = Battle.Shape(PrimitiveType.Quad);
            q.transform.SetParent(root);
            q.transform.position = p + Vector3.up * 0.01f;
            q.transform.rotation = Quaternion.Euler(90, R() * 360, 0);
            q.transform.localScale = new Vector3(size * (0.8f + R() * 0.5f), size * (0.8f + R() * 0.5f), 1);
            var mr = q.GetComponent<MeshRenderer>();
            mr.sharedMaterial = Battle.Mat("Dirt");
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }
        for (var side = 0; side < 4; side++)
        for (var t = -h; t <= h; t += 2.4f)
        {
            foreach (var off in new[] { 1.4f, -1.1f })
            {
                var d = h + off + (R() - 0.5f) * 0.8f;
                var p = side switch
                {
                    0 => new Vector3(t, 0, -d),
                    1 => new Vector3(d, 0, t),
                    2 => new Vector3(t, 0, d),
                    _ => new Vector3(-d, 0, t),
                };
                Patch(p, 2.6f + R() * 1.6f);
            }
        }
        // the old road the army comes along, from the open field to the near corner
        for (var k = 0f; k < 1f; k += 0.06f)
        {
            var p = Vector3.Lerp(new Vector3(-h - 22, 0, -h - 4), new Vector3(-h - 1, 0, -h + 9), k) + new Vector3((R() - 0.5f) * 1.2f, 0, (R() - 0.5f) * 1.2f);
            Patch(p, 3.2f + R() * 1.4f);
        }
        for (var i = 0; i < 16; i++) Patch(new Vector3((R() - 0.5f) * 2 * (h - 2), 0, (R() - 0.5f) * 2 * (h - 2)), 2.5f + R() * 2.5f);
    }

    // ------------------------------------------------------------------ the enemy town

    void BuildTown()
    {
        var town = new GameObject("Town").transform;
        Building(town, "town_hall", "Town hall", 5, 5, 4, 9000, 3);
        Building(town, "barracks", "Barracks", 1.2f, 9.6f, 3, 4000, 1);
        Building(town, "warehouse", "Warehouse", 9.8f, 9.6f, 3, 3600, 1);
        Building(town, "granary", "Granary", 9.8f, 1.4f, 3, 3600, 1);
        Building(town, "marketplace", "Marketplace", 1.2f, 1.4f, 3, 3200, 1);
        Building(town, "rally_point", "Rally point", 5.5f, 10.2f, 3, 2600, 1);
        Building(town, "archer_tower", "Archer tower", 1.4f, 6.2f, 2, 3400, 1, true);
        Building(town, "archer_tower", "Archer tower", 10.6f, 6.2f, 2, 3400, 1, true);
        Building(town, "archer_tower", "Archer tower", 6f, 1.2f, 2, 3400, 1, true);
    }

    Structure Building(Transform parent, string key, string label, float gx, float gy, int fp, float hp, float weight, bool tower = false)
    {
        var go = new GameObject(label);
        go.transform.SetParent(parent);
        var s = go.AddComponent<Structure>();
        s.Key = key;
        s.Label = label;
        s.MaxHp = s.Hp = hp;
        s.Weight = weight;
        s.IsTower = tower;
        var a = Battle.Grid(gx, gy);
        var b = Battle.Grid(gx + fp, gy + fp);
        s.Min = new Vector2(a.x, a.z);
        s.Max = new Vector2(b.x, b.z);
        // the picture's ground point is the footprint's front corner, the one nearest the camera
        var front = new Vector3(s.Min.x, 0, s.Min.y);
        s.Picture = Battle.Picture($"bld_germans_{key}_4", front, fp, s.Center, go.transform);
        var top = s.Picture != null ? s.Picture.GetComponent<MeshFilter>().sharedMesh.bounds.max.y : 6f;
        // a point h metres above the footprint's middle shows (0.354 fp tile + 0.866 h) up the screen from the front corner
        s.Height = Mathf.Max(1.5f, (top * 0.92f - 0.354f * fp * Battle.Tile) / 0.866f);
        s.FirePoint = s.Center + Vector3.up * Mathf.Max(2f, (top * 0.78f - 0.354f * fp * Battle.Tile) / 0.866f);
        var shrink = tower ? 0.5f : 0.72f;
        s.Occluder = Battle.Occluder(s.Center, new Vector3((s.Max.x - s.Min.x) * shrink, s.Height * (tower ? 0.95f : 0.8f), (s.Max.y - s.Min.y) * shrink), go.transform);
        Battle.Structures.Add(s);
        return s;
    }

    // ------------------------------------------------------------------ the palisade: one breakable stretch per square of edge

    void BuildWalls()
    {
        var root = new GameObject("Palisade").transform;
        var h = Battle.Half;
        var n = Battle.TownSize;
        for (var side = 0; side < 4; side++)
        {
            for (var i = 0; i < n; i++)
            {
                var t0 = -h + i * Battle.Tile;
                var t1 = t0 + Battle.Tile;
                Vector3 a, b, normal;
                switch (side)
                {
                    case 0: a = new Vector3(t0, 0, -h); b = new Vector3(t1, 0, -h); normal = Vector3.back; break;
                    case 1: a = new Vector3(h, 0, t0); b = new Vector3(h, 0, t1); normal = Vector3.right; break;
                    case 2: a = new Vector3(t1, 0, h); b = new Vector3(t0, 0, h); normal = Vector3.forward; break;
                    default: a = new Vector3(-h, 0, t1); b = new Vector3(-h, 0, t0); normal = Vector3.left; break;
                }
                Wall(root, a, b, normal, side * 100 + i);
            }
        }
    }

    void Wall(Transform parent, Vector3 a, Vector3 b, Vector3 normal, int seed)
    {
        var go = new GameObject("wall");
        go.transform.SetParent(parent);
        var s = go.AddComponent<Structure>();
        s.Key = "wall";
        s.IsWall = true;
        s.MaxHp = s.Hp = 1100;
        s.Weight = 0;
        s.Normal = normal;
        var lo = Vector3.Min(a, b) - new Vector3(0.35f, 0, 0.35f);
        var hi = Vector3.Max(a, b) + new Vector3(0.35f, 0, 0.35f);
        if (Mathf.Abs(normal.x) > 0) { lo.z += 0.35f; hi.z -= 0.35f; } else { lo.x += 0.35f; hi.x -= 0.35f; }
        s.Min = new Vector2(lo.x, lo.z);
        s.Max = new Vector2(hi.x, hi.z);
        s.Height = 2.6f;
        var rnd = new System.Random(seed);
        s.Intact = Palisade.Stretch(go.transform, a, b, normal, rnd, false);
        s.Ruin = Palisade.Stretch(go.transform, a, b, normal, rnd, true);
        s.Ruin.SetActive(false);
        Battle.Walls.Add(s);
        Battle.Structures.Add(s);
    }

    // ------------------------------------------------------------------ woods and rocks round the town

    void BuildNature()
    {
        var root = new GameObject("Nature").transform;
        var trees = new[] { "deco_pine_1", "deco_pine_2", "deco_oak_1", "deco_oak_2" };
        var under = new[] { "deco_bush", "deco_rock_1" };
        var rnd = new System.Random(11);
        float R() => (float)rnd.NextDouble();
        var placed = new List<Vector3>();
        for (var c = 0; c < 22; c++)
        {
            var ang = R() * Mathf.PI * 2;
            var rad = Battle.Half + 9f + R() * 22f;
            var cx = Mathf.Cos(ang) * rad;
            var cz = Mathf.Sin(ang) * rad;
            // keep the side facing the camera open for the army
            if (cx < -Battle.Half && cz < -Battle.Half + 6) continue;
            var count = 4 + rnd.Next(7);
            for (var i = 0; i < count; i++)
            {
                var a2 = R() * Mathf.PI * 2;
                var d = Mathf.Sqrt(R()) * 4.5f;
                var p = new Vector3(cx + Mathf.Cos(a2) * d, 0, cz + Mathf.Sin(a2) * d);
                if (Mathf.Abs(p.x) < Battle.Half + 6 && Mathf.Abs(p.z) < Battle.Half + 6) continue;
                if (placed.Any(q => (q - p).sqrMagnitude < 1.4f)) continue;
                placed.Add(p);
                var pick = R();
                var id = pick < 0.8f ? trees[rnd.Next(trees.Length)] : under[rnd.Next(under.Length)];
                var big = id.Contains("pine") || id.Contains("oak");
                var fit = (big ? 1.0f : 0.65f) + R() * 0.45f;
                Battle.Picture(id, p, fit, p, root, R() > 0.5f);
                if (big) Battle.Occluder(p, new Vector3(0.9f, 4.2f * fit, 0.9f) * Battle.Tile / 2, root);
            }
        }
    }

    void BuildBorder()
    {
        var go = new GameObject("Deploy border");
        border = go.AddComponent<LineRenderer>();
        border.loop = true;
        border.useWorldSpace = true;
        var m = Battle.Half + Battle.DeployMargin;
        border.positionCount = 4;
        border.SetPositions(new[] { new Vector3(-m, 0.06f, -m), new Vector3(m, 0.06f, -m), new Vector3(m, 0.06f, m), new Vector3(-m, 0.06f, m) });
        border.widthMultiplier = 0.14f;
        border.sharedMaterial = Battle.Mat("Line");
        border.shadowCastingMode = ShadowCastingMode.Off;
        border.alignment = LineAlignment.TransformZ;
        go.transform.rotation = Quaternion.Euler(90, 0, 0);
        border.enabled = false;
    }

    // ------------------------------------------------------------------ the flow of the battle

    IEnumerator Begin()
    {
        var troops = new List<(UnitKind, string, int, Texture)>
        {
            (UnitKind.Swordsman, "Knights", 24, Portrait(UnitKind.Swordsman)),
            (UnitKind.Archer, "Archers", 16, Portrait(UnitKind.Archer)),
            (UnitKind.Hero, "Hero", 1, Portrait(UnitKind.Hero)),
        };
        var uiGo = new GameObject("UI");
        ui = uiGo.AddComponent<BattleUI>();
        ui.Build(troops);
        ui.OnRetry = () => SceneManager.LoadScene(0);
        ui.OnEnd = () => End();
        ui.OnAbility = Ability;
        ui.ShowCards(false);
        ui.Tick(timeLeft);

        // the opening: the camera sweeps down onto the town, a horn, the title, then the drums
        rig.PlayIntro(4.6f);
        yield return new WaitForSeconds(0.35f);
        Battle.Sound("horn_attack", 0.9f);
        yield return new WaitForSeconds(0.6f);
        StartCoroutine(ui.Title("The Siege of Thorgard", 2.2f));
        yield return new WaitForSeconds(1.6f);
        music = gameObject.AddComponent<AudioSource>();
        music.clip = Battle.Clip("battle");
        music.loop = true;
        music.volume = 0;
        music.Play();
        StartCoroutine(Fade(music, 0.55f, 2.5f));
        yield return new WaitForSeconds(2.2f);
        ui.ShowCards(true);
        ui.Selected = 0;
        ui.Refresh();
        ui.Hint("Tap or hold outside the walls to send your troops", 5f);
        Battle.Started = true;
    }

    static Texture Portrait(UnitKind kind) => Resources.Load<Texture2D>("Portraits/" + kind);

    IEnumerator Fade(AudioSource src, float to, float time)
    {
        var from = src.volume;
        for (float t = 0; t < time; t += Time.deltaTime)
        {
            src.volume = Mathf.Lerp(from, to, t / time);
            yield return null;
        }
        src.volume = to;
    }

    void Update()
    {
        var t0 = Battle.Now;
        Tick();
        Battle.Spent("flow+ui", t0);
    }

    void Tick()
    {
        if (ui == null) return;
        if (Battle.Started && !ended)
        {
            timeLeft -= Time.deltaTime;
            var nothingLeft = ui.Cards.All(c => c.Count <= 0) && !Battle.Units.Any(u => u != null && u.Alive);
            if (Battle.Destruction >= 0.999f || timeLeft <= 0 || nothingLeft) End();
        }
        ui.Tick(timeLeft);
        if (border != null)
        {
            var show = Battle.Started && !ended && ui.Selected >= 0;
            border.enabled = show;
            borderRed = Mathf.Max(0, borderRed - Time.deltaTime);
            var c = borderRed > 0 ? new Color(1f, 0.25f, 0.2f, 0.9f) : new Color(1f, 1f, 1f, 0.42f);
            border.startColor = border.endColor = c;
        }
        HandleInput();
    }

    void End()
    {
        if (ended) return;
        ended = true;
        Battle.Over = true;
        var stars = Battle.Stars;
        var victory = stars > 0;
        if (music != null) StartCoroutine(Fade(music, 0f, 1.2f));
        Battle.Sound(victory ? "victory" : "defeat", 0.9f);
        foreach (var u in Battle.Units)
            if (u != null && u.Alive && victory) u.Cheer();
        StartCoroutine(ShowResult(victory, stars));
    }

    IEnumerator ShowResult(bool victory, int stars)
    {
        yield return new WaitForSeconds(1.6f);
        ui.Result(victory, stars, Battle.Destruction, Battle.Lost);
    }

    void Ability(BattleUI.Card card)
    {
        var hero = Battle.Units.FirstOrDefault(u => u != null && u.IsHero && u.Alive);
        if (hero == null) return;
        hero.Rally();
        card.AbilityReady = false;
        card.AbilityUsed = true;
        ui.Hint("The hero rallies the troops: healed and fighting harder", 3f);
        ui.Refresh();
    }

    // ------------------------------------------------------------------ touch and mouse

    void HandleInput()
    {
        if (!Battle.Started || ended) return;
        if (Input.touchCount >= 2)
        {
            var a = Input.GetTouch(0);
            var b = Input.GetTouch(1);
            var before = (a.position - a.deltaPosition - (b.position - b.deltaPosition)).magnitude;
            var now = (a.position - b.position).magnitude;
            if (before > 1 && now > 1) rig.Zoom(before / now, (a.position + b.position) / 2);
            rig.Pan((a.deltaPosition + b.deltaPosition) / 2);
            pointerDown = false;
            return;
        }
        bool down, up, held;
        Vector2 pos;
        int id;
        if (Input.touchCount == 1)
        {
            var t = Input.GetTouch(0);
            pos = t.position;
            id = t.fingerId;
            down = t.phase == TouchPhase.Began;
            up = t.phase is TouchPhase.Ended or TouchPhase.Canceled;
            held = !down && !up;
        }
        else
        {
            pos = Input.mousePosition;
            id = -1;
            down = Input.GetMouseButtonDown(0);
            up = Input.GetMouseButtonUp(0);
            held = Input.GetMouseButton(0) && !down;
            var wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f) rig.Zoom(Mathf.Pow(0.9f, wheel), pos);
            if (Input.GetMouseButton(1) || Input.GetMouseButton(2)) rig.Pan(pos - lastMouse);
            lastMouse = pos;
        }
        if (down)
        {
            if (BattleUI.OverUi(id)) { pointerDown = false; return; }
            pointerDown = true;
            panning = deploying = false;
            downPos = lastPos = pos;
            downTime = Time.time;
        }
        if (!pointerDown) return;
        if (down || held)
        {
            var moved = (pos - downPos).magnitude;
            if (!deploying && !panning)
            {
                if (moved > Screen.height * 0.02f) panning = true;
                else if (Time.time - downTime > 0.16f && ui.Selected >= 0) { deploying = true; nextDeploy = 0; }
            }
            if (panning) rig.Pan(pos - lastPos);
            if (deploying && Time.time >= nextDeploy)
            {
                Deploy(pos);
                nextDeploy = Time.time + 0.12f;
            }
            lastPos = pos;
        }
        if (up)
        {
            if (!panning && !deploying) Deploy(pos);
            pointerDown = panning = deploying = false;
        }
    }

    void Deploy(Vector2 screen)
    {
        if (ui.Selected < 0)
        {
            ui.Hint("Choose troops below first", 2f);
            return;
        }
        var card = ui.Cards[ui.Selected];
        if (card.Count <= 0) return;
        var p = rig.Ground(screen);
        if (!Battle.CanDeploy(p))
        {
            borderRed = 0.6f;
            ui.Hint("Send your troops outside the walls", 2f);
            return;
        }
        p += new Vector3(Random.Range(-0.35f, 0.35f), 0, Random.Range(-0.35f, 0.35f));
        var u = Unit.Create(card.Kind, p, Vector3.zero);
        Fx.Ring(p, card.Kind == UnitKind.Hero ? 4f : 2f, new Color(1f, 0.85f, 0.45f, 0.8f), 0.5f);
        Fx.Dust(p, 0.35f, 6);
        if (Time.time - lastMarch > 0.7f)
        {
            Battle.Sound("march", 0.35f);
            lastMarch = Time.time;
        }
        card.Count--;
        if (card.Kind == UnitKind.Hero)
        {
            card.AbilityReady = true;
            Battle.Sound("horn_attack", 0.6f);
            ui.Hint("Tap the hero card again to rally the troops", 3f);
        }
        if (card.Count <= 0)
        {
            var next = ui.Cards.FindIndex(c => c.Count > 0);
            ui.Selected = next;
            pointerDown = deploying = false;
        }
        ui.Refresh();
        _ = u;
    }
}
