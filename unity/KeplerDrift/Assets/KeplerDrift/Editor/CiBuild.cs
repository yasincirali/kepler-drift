// Unity side of the TestFlight pipeline (adapted from Birdie Smash CiBuild): game-ci/unity-builder calls this method via
// `buildMethod`, the output is an Xcode project; signing, build number and upload live in the fastlane `beta_unity` lane on
// macOS (the Unity side never sees App Store Connect).
// Why settings are here and not in ProjectSettings.asset: the repo has no ProjectSettings.asset (Unity generates it with
// defaults on first open); a hand-written asset that disagrees with the Unity version breaks silently. At build time
// PlayerSettings, scene, URP pipeline and input setting are written from code so a fresh CI checkout and the Editor agree.
// ITSAppUsesNonExemptEncryption=false is written to Info.plist here too (fastlane also writes it): without the key the build
// waits in App Store Connect under "Missing Compliance" and cannot be tested.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace KeplerDrift.EditorTools
{
    [InitializeOnLoad]
    public static class CiBuild
    {
        // Must match fastlane Appfile/Matchfile/Fastfile and the App Store Connect app record.
        const string Bundle = "com.keplerdrift.app";
        const string ProductName = "Kepler Drift";
        // Single source of the version name (no pubspec in a Flutter-less repo): fastlane reads it from the Info.plist Unity
        // writes and takes the build number from TestFlight (latest + 1). For a new version only this is bumped.
        const string Version = "0.1.0";
        const string ScenePath = "Assets/Scenes/Mission.unity";
        const string UrpFolder = "Assets/KeplerDrift/Settings";
        const string Renderer2D = UrpFolder + "/KeplerDrift2DRenderer.asset";
        const string UrpAsset = UrpFolder + "/KeplerDriftURP.asset";

        // On Editor open the scene is set up if missing (Birdie DemoSetup pattern): the first person to open it must not press Play on an empty scene.
        static CiBuild() { EditorApplication.delayCall += () => { if (!Application.isBatchMode && !File.Exists(ScenePath)) Setup(); }; }

        // IL2CPP crash trace (Birdie 2026-10-29): `il2cpp --convert-to-cpp` occasionally dies with SIGSEGV on the runner. If
        // "il2cpp" and "Segmentation fault" appear in the same log message, il2cpp_crash.txt is written next to the build folder;
        // ios-testflight.yml retries once only if that file exists. A C# compile error carries no such trace and is not
        // retried: the real error must not be hidden.
        static bool il2cppCrash;
        static void OnLog(string msg, string trace, LogType type)
        {
            if (type == LogType.Log || type == LogType.Warning || msg == null) return;
            if (msg.IndexOf("il2cpp", StringComparison.OrdinalIgnoreCase) >= 0 && msg.IndexOf("Segmentation fault", StringComparison.Ordinal) >= 0) il2cppCrash = true;
        }

        [MenuItem("Kepler Drift/Set up scene and settings")]
        public static void Setup()
        {
            Urp();
            Directory.CreateDirectory("Assets/Scenes");
            if (!File.Exists(ScenePath))
            {
                var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                // Camera + root: GameRoot builds the rest (bodies, HUD, sound) from code at runtime; the scene file is not
                // edited by hand so the CI one and the Editor one stay identical.
                var camGo = new GameObject("Camera"); camGo.tag = "MainCamera"; var c = camGo.AddComponent<Camera>(); c.orthographic = true;
                camGo.transform.position = new Vector3(0, 0, -10);
                new GameObject("GameRoot").AddComponent<KeplerDrift.Game.GameRoot>();
                EditorSceneManager.SaveScene(s, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            if (!Application.isBatchMode && SceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
            // Shaders found at runtime via Shader.Find must ship in the package (no material assets, they are created from code).
            IncludeShader("KeplerDrift/Planet"); IncludeShader("Sprites/Default");
            IncludeShader("Hidden/UIE-Runtime"); IncludeShader("Hidden/UIE-AtlasBlit"); IncludeShader("Hidden/UIE-RuntimeWorld");
            InputHandling();
            AssetDatabase.SaveAssets();
            Debug.Log("Kepler Drift: " + ScenePath + " ready.");
        }

        // URP 2D renderer: generated from code if the asset is not in the repo (same reasoning as ProjectSettings). Why the 2D
        // renderer: the scene is all sprites; the 3D forward renderer's shadow/depth passes only waste battery on mobile.
        static void Urp()
        {
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAsset);
            if (rp == null)
            {
                Directory.CreateDirectory(UrpFolder);
                try
                {
                    var rd = AssetDatabase.LoadAssetAtPath<Renderer2DData>(Renderer2D);
                    if (rd == null) { rd = ScriptableObject.CreateInstance<Renderer2DData>(); AssetDatabase.CreateAsset(rd, Renderer2D); }
                    rp = UniversalRenderPipelineAsset.Create(rd);
                    AssetDatabase.CreateAsset(rp, UrpAsset);
                    Debug.Log("Kepler Drift: URP 2D pipeline generated (" + UrpAsset + ")");
                }
                catch (Exception e) { Debug.LogError("Kepler Drift: could not generate URP asset: " + e.Message); return; }
            }
            GraphicsSettings.defaultRenderPipeline = rp;
            // Same pipeline for every quality level: a level left empty falls back to the default (built-in) RP and the URP
            // shader (Planet) renders pink.
            int previous = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = rp; }
            QualitySettings.SetQualityLevel(previous, false);
        }

        static void IncludeShader(string name)
        {
            var sh = Shader.Find(name); if (sh == null) { Debug.LogWarning("Kepler Drift: shader missing: " + name); return; }
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (gs == null || gs.Length == 0) return;
            var so = new SerializedObject(gs[0]); var arr = so.FindProperty("m_AlwaysIncludedShaders"); if (arr == null) return;
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) return;
            arr.InsertArrayElementAtIndex(arr.arraySize); arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh; so.ApplyModifiedProperties();
        }

        // Input: the Input System package is installed; project setting "Both" (2). Why not "Input System only" (1): the setting
        // takes effect after an Editor restart; on a fresh CI checkout the first compile may run with the old backend, and
        // GameRoot falls back to the old Input without ENABLE_INPUT_SYSTEM. "Both" compiles in both cases.
        static void InputHandling()
        {
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (ps == null || ps.Length == 0) return;
            var so = new SerializedObject(ps[0]); var p = so.FindProperty("activeInputHandler");
            if (p != null && p.intValue != 2) { p.intValue = 2; so.ApplyModifiedProperties(); Debug.Log("Kepler Drift: input set to 'Both'"); }
        }

        public static void iOS()
        {
            string path = Arg("-customBuildPath") ?? "build/iOS/iOS";
            Application.logMessageReceivedThreaded += OnLog;
            try
            {
                Setup();
                Configure(Arg("-buildVersion"));
                var opt = new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                    locationPathName = path, target = BuildTarget.iOS, targetGroup = BuildTargetGroup.iOS, options = BuildOptions.None
                };
                if (opt.scenes.Length == 0) throw new Exception("No scene in the build list (CiBuild.Setup)");
                var rep = BuildPipeline.BuildPlayer(opt);
                if (rep.summary.result != BuildResult.Succeeded) throw new Exception("BuildPlayer: " + rep.summary.result + ", errors " + rep.summary.totalErrors);
                PlistKey(Path.Combine(path, "Info.plist"), "ITSAppUsesNonExemptEncryption", "<false/>");
                PlistKey(Path.Combine(path, "Info.plist"), "CADisableMinimumFrameDurationOnPhone", "<true/>");   // ProMotion (Apple requirement)
                Debug.Log("Kepler Drift iOS Xcode project: " + path);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("Kepler Drift CI build failed: " + e);
                if (il2cppCrash)
                    try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), "il2cpp_crash.txt"), "il2cpp Segmentation fault\n"); }
                    catch (Exception e2) { Debug.LogWarning("could not write il2cpp_crash.txt: " + e2.Message); }
                EditorApplication.Exit(1);
            }
        }

        // Screenshot pipeline (unity-screenshots.yml): the same scene as a Linux player (Mono, fast compile); CI runs it under
        // xvfb with `-screenshots` (Game/ScreenshotTour). Never goes to the store.
        public static void Linux()
        {
            try
            {
                string path = Arg("-customBuildPath") ?? "build/StandaloneLinux64/KeplerDrift";
                Setup();
                PlayerSettings.productName = ProductName; PlayerSettings.companyName = ProductName;
#pragma warning disable 618
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
#pragma warning restore 618
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed; PlayerSettings.resizableWindow = true;
                PlayerSettings.runInBackground = true;   // no window manager under xvfb, focus never arrives: otherwise the player stops on the first frame (Birdie)
                PlayerSettings.defaultScreenWidth = 420; PlayerSettings.defaultScreenHeight = 880;
                var opt = new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                    locationPathName = path, target = BuildTarget.StandaloneLinux64, targetGroup = BuildTargetGroup.Standalone, options = BuildOptions.None
                };
                var rep = BuildPipeline.BuildPlayer(opt);
                if (rep.summary.result != BuildResult.Succeeded) throw new Exception("BuildPlayer: " + rep.summary.result + ", errors " + rep.summary.totalErrors);
                Debug.Log("Kepler Drift Linux player: " + path);
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogError("Kepler Drift Linux build failed: " + e); EditorApplication.Exit(1); }
        }

#pragma warning disable 618   // BuildTargetGroup signatures are considered obsolete in Unity 6 (warning); same family as Birdie
        static void Configure(string version)
        {
            PlayerSettings.productName = ProductName; PlayerSettings.companyName = ProductName;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, Bundle);
            // Version name and build number are rewritten into Info.plist by fastlane (latest TestFlight number + 1).
            PlayerSettings.bundleVersion = !string.IsNullOrEmpty(version) && version != "none" ? version : Version;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.iOS, ScriptingImplementation.IL2CPP);   // mandatory on iOS
            // Portrait only: the world is a 420 px wide vertical strip; in landscape the route does not fit the screen.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true; PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false; PlayerSettings.allowedAutorotateToLandscapeRight = false;
            // iOS 15 minimum (Birdie 2026-10-26, ITMS-90068: from April 2027 builds below 15.0 cannot be uploaded).
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            // Full screen is required for a portrait lock on iPad (Apple: an app supporting multitasking must allow all orientations).
            PlayerSettings.iOS.requiresFullScreen = true;
            PlayerSettings.iOS.appleEnableAutomaticSigning = false;   // signing comes from fastlane match; Xcode must not attempt automatic signing
            PlayerSettings.statusBarHidden = true;
            PlayerSettings.muteOtherAudioSources = false;   // the player's own music keeps playing (Ambient session)
            // Launch: Unity splash off (free in Unity 6), background is the game's base colour (#05070d) so there is no colour
            // jump into the first frame (Apple HIG: the launch screen resembles the first frame).
            PlayerSettings.SplashScreen.show = false; PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.backgroundColor = new Color32(0x05, 0x07, 0x0d, 0xff);
            // TODO(icon): once Assets/KeplerDrift/Icon/AppIcon.png (1024, no alpha) exists, SetIconsForTargetGroup(Unknown, …).
        }
#pragma warning restore 618

        // Edits the plist as text: UnityEditor.iOS.Xcode only compiles when the iOS module is installed; this file must not
        // break in an Editor without it.
        static void PlistKey(string plist, string key, string value)
        {
            if (!File.Exists(plist)) { Debug.LogWarning("Info.plist missing: " + plist); return; }
            var t = File.ReadAllText(plist);
            if (t.Contains("<key>" + key + "</key>")) return;
            int i = t.LastIndexOf("</dict>", StringComparison.Ordinal); if (i < 0) return;
            File.WriteAllText(plist, t.Substring(0, i) + "\t<key>" + key + "</key>\n\t" + value + "\n" + t.Substring(i));
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }
    }
}
