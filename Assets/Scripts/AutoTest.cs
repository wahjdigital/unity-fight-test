using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Started with -autotest &lt;folder&gt;: plays a battle by itself (deploys, rallies) and saves screenshots along the way,
/// so the look and the fighting can be checked without a hand on the screen.
/// </summary>
public class AutoTest : MonoBehaviour
{
    string folder;
    int shot;

    public static void StartIfAsked(GameObject host)
    {
        var args = System.Environment.GetCommandLineArgs();
        var i = System.Array.IndexOf(args, "-autotest");
        // phones do not pass launch arguments through: an environment variable starts it there
        var env = System.Environment.GetEnvironmentVariable("FIGHT_AUTOTEST");
        if ((i < 0 || i + 1 >= args.Length) && string.IsNullOrEmpty(env)) return;
        var t = host.AddComponent<AutoTest>();
        var where = i >= 0 && i + 1 < args.Length ? args[i + 1] : "persistent";
        t.folder = where == "persistent" ? Application.persistentDataPath : where;
        Directory.CreateDirectory(t.folder);
    }

    IEnumerator Start()
    {
        StartCoroutine(Fps());
        yield return new WaitForSeconds(1.2f);
        Shot("intro");
        yield return new WaitForSeconds(1.5f);
        SavePortraits();
        yield return new WaitUntil(() => Battle.Started);
        yield return new WaitForSeconds(0.5f);
        Shot("ready");
        var boot = FindFirstObjectByType<BattleBootstrap>();
        // knights at the front corner, archers behind them, the hero on the left flank
        for (var i = 0; i < 12; i++) { boot.DeployAt(0, Battle.Grid(-1.6f, 3f + i * 0.5f)); yield return new WaitForSeconds(0.1f); }
        for (var i = 0; i < 8; i++) { boot.DeployAt(1, Battle.Grid(-3.6f, 3.5f + i * 0.6f)); yield return new WaitForSeconds(0.1f); }
        boot.DeployAt(2, Battle.Grid(-2f, 8f));
        yield return new WaitForSeconds(2.5f);
        Shot("charge");
        BattleCam(10f, Battle.Grid(0.5f, 6f));
        yield return new WaitForSeconds(3f);
        Shot("walls");
        for (var i = 0; i < 12; i++) { boot.DeployAt(0, Battle.Grid(3f + i * 0.5f, -1.6f)); yield return new WaitForSeconds(0.1f); }
        for (var i = 0; i < 8; i++) { boot.DeployAt(1, Battle.Grid(3.5f + i * 0.6f, -3.6f)); yield return new WaitForSeconds(0.1f); }
        yield return new WaitForSeconds(5f);
        Shot("fight");
        boot.RallyNow();
        yield return new WaitForSeconds(1f);
        Shot("rally");
        BattleCam(15.5f, new Vector3(-2, 0, -2));
        for (var k = 0; k < 6; k++)
        {
            yield return new WaitForSeconds(8f);
            Shot("battle" + k);
            if (Battle.Over) break;
        }
        yield return new WaitUntil(() => Battle.Over);
        yield return new WaitForSeconds(4f);
        Shot("result");
        yield return new WaitForSeconds(0.5f);
        Application.Quit();
    }

    void SavePortraits()
    {
        var ui = FindFirstObjectByType<BattleUI>();
        if (ui == null) return;
        foreach (var c in ui.Cards)
        {
            if (c.Portrait.texture is not RenderTexture rt) continue;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(folder, "portrait_" + c.Name + ".png"), tex.EncodeToPNG());
        }
    }

    void BattleCam(float size, Vector3 focus)
    {
        CameraRig.Instance.Size = size;
        CameraRig.Instance.Focus = focus;
    }

    void Shot(string name)
    {
        var file = $"{shot++:00}_{name}.png";
        var path = Path.Combine(folder, file);
        // phones take a bare file name and put it in the app's documents
        ScreenCapture.CaptureScreenshot(Application.isMobilePlatform ? file : path);
        Debug.Log($"SHOT {path} units {Battle.Units.Count(u => u != null && u.Alive)} destruction {Battle.Destruction:P0}");
    }

    IEnumerator Fps()
    {
        while (true)
        {
            var frames = 0;
            var t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 5f) { frames++; yield return null; }
            FrameTimingManager.CaptureFrameTimings();
            var ft = new FrameTiming[1];
            var got = FrameTimingManager.GetLatestTimings(1, ft);
            var particles = 0;
            var systems = FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
            foreach (var p in systems) particles += p.particleCount;
            var arrows = FindObjectsByType<Arrow>(FindObjectsSortMode.None).Length;
            var costs = string.Join(", ", Battle.Cost.Select(kv => $"{kv.Key} {kv.Value / frames:F2}ms"));
            Battle.Cost.Clear();
            Debug.Log($"COST per frame: {costs}");
            Debug.Log($"FPS {frames / (Time.realtimeSinceStartup - t0):F1} cpu {(got > 0 ? ft[0].cpuFrameTime : -1):F1}ms gpu {(got > 0 ? ft[0].gpuFrameTime : -1):F1}ms particles {particles} in {systems.Length} systems, arrows {arrows}, objects {FindObjectsByType<Transform>(FindObjectsSortMode.None).Length}");
        }
    }
}
