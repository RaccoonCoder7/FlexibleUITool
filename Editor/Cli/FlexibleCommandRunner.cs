using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlexibleUITool.EditorTools
{
    // CLI 명령 처리 (에디터 브리지가 호출). 결과는 항상 JSON 한 덩어리: ok / error{code,message} / warnings
    public static class FlexibleCommandRunner
    {
        private delegate void Handler(FlexibleCommand command, FlexibleJson json, List<string> warnings, bool inEditor);

        private static readonly Dictionary<string, Handler> Handlers = new Dictionary<string, Handler>
        {
            { "ping", Ping },
            { "profiles", Profiles },
            { "list", List },
            { "describe", Describe },
            { "render", Render },
            { "check", Check },
            { "export-spec", ExportSpec },
            { "apply-spec", ApplySpec },
            { "set-role", SetRole },
            { "duplicate", Duplicate },
            { "clear-profile", ClearProfile },
            { "scene-place", ScenePlace },
            { "scene-clear", SceneClear },
            { "game-view", GameView },
            { "review", Review },
            { "cleanup-tutorial", CleanupTutorial },
            { "import-samples", ImportSamples },
        };

        private const string SamplePrefabName = FlexibleSettingsUtility.SamplePrefabName;

        // 튜토리얼 샘플 폴더 (FlexibleSample 프리팹이 있는 곳). 없으면 null
        public static string SampleFolder => FlexibleSettingsUtility.FindSampleFolder();

        // 샘플 폴더의 SampleScene
        public static string DefaultTestScene => SampleFolder != null ? SampleFolder + "/SampleScene.unity" : null;
        private const string TestInstancePrefix = "[FlexibleTest] ";
        private const string PreviousSceneKey = "FlexibleUITool.PreviousScene";

        public static string ExecuteJson(string commandJson, string fallbackId, bool inEditor, out bool ok)
        {
            FlexibleCommand command;
            try
            {
                command = JsonUtility.FromJson<FlexibleCommand>(commandJson);
            }
            catch (Exception e)
            {
                ok = false;
                return Error(fallbackId, null, "BAD_REQUEST", "Command is not valid JSON: " + e.Message);
            }

            if (command == null)
            {
                ok = false;
                return Error(fallbackId, null, "BAD_REQUEST", "Empty command.");
            }

            if (string.IsNullOrEmpty(command.id))
                command.id = fallbackId;
            return Execute(command, inEditor, out ok);
        }

        public static string Execute(FlexibleCommand command, bool inEditor, out bool ok)
        {
            ok = false;
            if (string.IsNullOrEmpty(command.command) || !Handlers.TryGetValue(command.command, out Handler handler))
                return Error(command.id, command.command, "UNKNOWN_COMMAND", $"Unknown command '{command.command}'. Available: {string.Join(", ", Handlers.Keys)}");

            var warnings = new List<string>();
            var json = new FlexibleJson().BeginObject()
                .Value("id", command.id)
                .Value("command", command.command)
                .Value("ok", true);
            try
            {
                // CLI 결과는 툴 UI 언어와 무관하게 영어
                using (FlexibleText.ForceEnglish())
                    handler(command, json, warnings, inEditor);
            }
            catch (FlexibleCommandException e)
            {
                return Error(command.id, command.command, e.Code, e.Message);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return Error(command.id, command.command, "INTERNAL", e.GetType().Name + ": " + e.Message);
            }

            ok = true;
            return json.Strings("warnings", warnings).EndObject().ToString();
        }

        public static string Error(string id, string command, string code, string message)
        {
            return new FlexibleJson().BeginObject()
                .Value("id", id)
                .Value("command", command)
                .Value("ok", false)
                .BeginObject("error").Value("code", code).Value("message", message).EndObject()
                .EndObject().ToString();
        }

        // ───────── 명령 ─────────

        private static void Ping(FlexibleCommand command, FlexibleJson json, List<string> warnings, bool inEditor)
        {
            json.Value("mode", inEditor ? "editor" : "batch")
                .Value("unity", Application.unityVersion)
                .Value("project", Directory.GetParent(Application.dataPath)?.FullName);
        }

        private static void Profiles(FlexibleCommand command, FlexibleJson json, List<string> warnings, bool inEditor)
        {
            FlexibleProfileSettings settings = FlexibleSettingsUtility.GetOrCreateDefault();
            json.Value("baseProfile", settings.BaseProfileId)
                .Value("hysteresis", settings.Hysteresis)
                .Value("referenceShortSide", settings.ReferenceShortSide)
                .BeginArray("profiles");
            for (int i = 0; i < settings.Profiles.Count; i++)
            {
                FlexibleProfile profile = settings.Profiles[i];
                Vector2 work = FlexibleSpecService.GetWorkSize(profile, settings);
                Vector4 insets = settings.GetSafeInsets(i);
                float max = settings.MaxAspectOf(i);
                json.BeginObject()
                    .Value("id", profile.id)
                    .Value("name", profile.displayName)
                    .Value("minAspect", profile.minAspect)
                    .Value("maxAspect", float.IsPositiveInfinity(max) ? -1f : max)
                    .Numbers("workSize", work.x, work.y)
                    .BeginObject("grid")
                    .Value("columns", profile.grid.columns).Value("rows", profile.grid.rows)
                    .Value("margin", profile.grid.margin).Value("gutter", profile.grid.gutter)
                    .EndObject()
                    .Numbers("safeInsets", insets.x, insets.y, insets.z, insets.w)
                    .EndObject();
            }
            json.EndArray().BeginArray("presets");
            for (int i = 0; i < settings.PreviewPresets.Count; i++)
            {
                FlexiblePreviewPreset preset = settings.PreviewPresets[i];
                json.BeginObject()
                    .Value("index", i)
                    .Value("name", preset.name)
                    .Numbers("resolution", preset.resolution.x, preset.resolution.y)
                    .Value("aspect", preset.Aspect)
                    .Value("profile", settings.Profiles[settings.SelectProfile(preset.Aspect, -1)].id)
                    .Numbers("safeInsets", preset.safeLeft, preset.safeRight, preset.safeTop, preset.safeBottom)
                    .EndObject();
            }
            json.EndArray().Strings("roles", settings.Roles).BeginArray("templates");
            foreach (FlexibleTemplate template in settings.Templates)
            {
                json.BeginObject().Value("name", template.name).Value("profile", template.profileId).BeginArray("slots");
                foreach (FlexibleTemplateSlot slot in template.slots)
                {
                    json.BeginObject().Value("role", slot.role)
                        .Numbers("cell", slot.cell.column, slot.cell.row, slot.cell.width, slot.cell.height)
                        .Value("modeX", FlexibleSpecService.ModeName(slot.modeX))
                        .Value("modeY", FlexibleSpecService.ModeName(slot.modeY))
                        .Value("safeArea", slot.insideSafeArea);
                    if (slot.HasAdjustment)
                        json.Numbers("offset", slot.positionOffset.x, slot.positionOffset.y).Numbers("sizeOffset", slot.sizeOffset.x, slot.sizeOffset.y);
                    json.EndObject();
                }
                json.EndArray().EndObject();
            }
            json.EndArray();
        }

        private static void List(FlexibleCommand command, FlexibleJson json, List<string> warnings, bool inEditor)
        {
            json.BeginArray("prefabs");
            foreach (string path in FlexibleReviewPanel.FindTargets(string.IsNullOrEmpty(command.folder) ? "Assets" : command.folder))
            {
                var layout = AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponent<FlexibleLayout>();
                json.BeginObject().Value("path", path);
                if (layout != null)
                {
                    json.Value("roles", layout.Roles.Count).BeginArray("profiles");
                    foreach (string profileId in layout.Entries.SelectMany(e => e.states).Select(s => s.profileId).Distinct())
                    {
                        List<FlexibleProfileState> states = layout.Entries.Select(e => e.Find(profileId)).Where(s => s != null).ToList();
                        json.BeginObject().Value("profile", profileId).Value("records", states.Count)
                            .Value("grid", states.Count(s => s.gridPlaced)).Value("manual", states.Count(s => !s.gridPlaced)).EndObject();
                    }
                    json.EndArray();
                }
                json.EndObject();
            }
            json.EndArray();
        }

        // 프로필 작업 해상도로 사본을 배치해 블록 위치(좌상단 기준)·배치 방식·문제를 돌려준다
        private static void Describe(FlexibleCommand command, FlexibleJson json, List<string> warnings, bool inEditor)
        {
            string prefabPath = ResolvePrefab(command.prefab);
            WarnIfOpenAndDirty(prefabPath, inEditor, warnings);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var layout = asset.GetComponent<FlexibleLayout>();
            FlexibleProfileSettings settings = layout != null && layout.Settings != null ? layout.Settings : FlexibleSettingsUtility.GetOrCreateDefault();
            if (layout == null)
                warnings.Add("Prefab has no FlexibleLayout; only the base layout exists.");

            json.Value("prefab", prefabPath).BeginArray("profiles");
            using (var renderer = new FlexiblePreviewRenderer())
            {
                foreach (FlexibleProfile profile in SelectProfiles(settings, command.profile))
                {
                    Vector2 size = FlexibleSpecService.GetWorkSize(profile, settings);
                    renderer.Prepare(asset, profile.id, size, new Rect(0f, 0f, 1f, 1f));
                    var root = (RectTransform)renderer.Instance.transform;
                    int index = settings.IndexOf(profile.id);
                    Vector4 insets = settings.GetSafeInsets(index);

                    json.BeginObject().Value("profile", profile.id).Value("isBase", profile.id == settings.BaseProfileId)
                        .Numbers("canvas", size.x, size.y)
                        .BeginObject("grid").Value("columns", profile.grid.columns).Value("rows", profile.grid.rows)
                        .Value("margin", profile.grid.margin).Value("gutter", profile.grid.gutter)
                        .Numbers("safeInsets", insets.x, insets.y, insets.z, insets.w).EndObject()
                        .BeginArray("blocks");
                    foreach (RectTransform block in SelectBlocks(root, command.all))
                        WriteBlockInfo(json, root, block, layout, (RectTransform)asset.transform, profile, settings);
                    json.EndArray();
                    WriteIssues(json, renderer.CollectIssueList(new Rect(0f, 0f, 1f, 1f)));
                    json.EndObject();
                }
            }
            json.EndArray();
        }

        private static void WriteBlockInfo(FlexibleJson json, RectTransform root, RectTransform block, FlexibleLayout layout,
            RectTransform assetRoot, FlexibleProfile profile, FlexibleProfileSettings settings)
        {
            string path = FlexiblePathUtility.GetPath(root, block);
            Rect rootRect = root.rect;
            Rect r = FlexiblePreviewRenderer.GetRectInRoot(root, block);
            float left = r.xMin - rootRect.xMin;
            float top = rootRect.yMax - r.yMax;

            json.BeginObject().Value("path", path);
            FlexibleLayoutEntry entry = null;
            if (layout != null)
            {
                FlexiblePathUtility.BuildMap(assetRoot).TryGetValue(path, out RectTransform assetTarget);
                string role = assetTarget != null ? layout.GetRole(assetTarget) : null;
                if (!string.IsNullOrEmpty(role))
                    json.Value("role", role);
                entry = assetTarget != null ? layout.Entries.Find(e => e.target == assetTarget) : null;
            }

            json.Value("active", block.gameObject.activeInHierarchy)
                .Numbers("rect", Round(left), Round(top), Round(r.width), Round(r.height))
                .Numbers("normalized", Round3(left / rootRect.width), Round3(top / rootRect.height),
                    Round3((left + r.width) / rootRect.width), Round3((top + r.height) / rootRect.height));

            FlexibleProfileState state = entry?.Find(profile.id);
            if (profile.id == settings.BaseProfileId || state == null)
            {
                json.Value("layout", "base");
            }
            else if (state.gridOrigin && FlexibleGridPlacementService.TryGetAdjustment(state.rect, state.gridResult, entry.target.localScale, out FlexibleGridAdjustment adjustment))
            {
                json.Value("layout", adjustment.IsZero ? "grid" : "adjusted")
                    .Numbers("cell", state.gridCell.column, state.gridCell.row, state.gridCell.width, state.gridCell.height)
                    .Value("modeX", FlexibleSpecService.ModeName(state.gridModeX))
                    .Value("modeY", FlexibleSpecService.ModeName(state.gridModeY))
                    .Value("safeArea", state.gridInsideSafeArea);
                if (adjustment.position.sqrMagnitude > 0.0001f)
                    json.Numbers("offset", adjustment.position.x, adjustment.position.y);
                if (adjustment.size.sqrMagnitude > 0.0001f)
                    json.Numbers("sizeOffset", adjustment.size.x, adjustment.size.y);
                if (adjustment.overrideScale)
                    json.Numbers("scale", adjustment.scale.x, adjustment.scale.y, adjustment.scale.z);
            }
            else
            {
                json.Value("layout", "manual");
            }
            json.EndObject();
        }

        private static void Render(FlexibleCommand command, FlexibleJson json, List<string> warnings, bool inEditor)
        {
            string prefabPath = ResolvePrefab(command.prefab);
            WarnIfOpenAndDirty(prefabPath, inEditor, warnings);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            FlexibleProfileSettings settings = GetSettings(asset);
            string folder = string.IsNullOrEmpty(command.output) ? Path.Combine(FlexibleCommandBridge.WorkFolder, "renders") : Path.GetFullPath(command.output);

            json.Value("prefab", prefabPath).BeginArray("images");
            using (var renderer = new FlexiblePreviewRenderer())
            {
                foreach (ScreenTarget target in ResolveTargets(settings, command))
                {
                    int profileIndex = target.ProfileIndex(settings);
                    Vector2 logical = FlexiblePreviewRenderer.GetLogicalSize(target.aspect, settings.ReferenceShortSide);
                    float scale = 1280f / Mathf.Max(logical.x, logical.y);
                    var pixel = new Vector2Int(Mathf.RoundToInt(logical.x * scale), Mathf.RoundToInt(logical.y * scale));
                    renderer.Render(asset, settings.Profiles[profileIndex].id, logical, pixel, target.safeArea);

                    string file = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(prefabPath)}__{Sanitize(target.name)}.png");
                    renderer.SavePng(file);
                    json.BeginObject().Value("target", target.name).Value("profile", settings.Profiles[profileIndex].id)
                        .Value("path", file).Numbers("size", pixel.x, pixel.y);
                    WriteIssues(json, renderer.CollectIssueList(target.safeArea));
                    json.EndObject();
                }
            }
            json.EndArray();
        }

        private static void Check(FlexibleCommand command, FlexibleJson json, List<string> warnings, bool inEditor)
        {
            List<string> prefabs = string.IsNullOrEmpty(command.prefab)
                ? FlexibleReviewPanel.FindTargets(string.IsNullOrEmpty(command.folder) ? "Assets" : command.folder)
                : new List<string> { ResolvePrefab(command.prefab) };

            int total = 0;
            json.BeginArray("results");
            using (var renderer = new FlexiblePreviewRenderer())
            {
                foreach (string prefabPath in prefabs)
                {
                    GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    FlexibleProfileSettings settings = GetSettings(asset);
                    renderer.Dirty = true;
                    foreach (ScreenTarget target in ResolveTargets(settings, command, defaultToPresets: true))
                    {
                        string profileId = settings.Profiles[target.ProfileIndex(settings)].id;
                        renderer.Prepare(asset, profileId, FlexiblePreviewRenderer.GetLogicalSize(target.aspect, settings.ReferenceShortSide), target.safeArea);
                        List<FlexibleIssue> issues = renderer.CollectIssueList(target.safeArea);
                        total += issues.Count;
                        json.BeginObject().Value("prefab", prefabPath).Value("target", target.name).Value("profile", profileId);
                        WriteIssues(json, issues);
                        json.EndObject();
                    }
                }
            }
            json.EndArray().Value("issueCount", total);
        }

        private static void ExportSpec(FlexibleCommand command, FlexibleJson json, List<string> warnings, bool inEditor)
        {
            string prefabPath = ResolvePrefab(command.prefab);
            WarnIfOpenAndDirty(prefabPath, inEditor, warnings);
            if (!string.IsNullOrEmpty(command.output))
            {
                var spec = new FlexibleJson();
                FlexibleSpecService.WriteSpec(spec.BeginObject(), prefabPath, command.profile, warnings);
                string text = spec.EndObject().ToString();
                // {"spec":{...}} 의 안쪽만 파일로 쓴다
                text = text.Substring("{\"spec\":".Length, text.Length - "{\"spec\":".Length - 1);
                File.WriteAllText(Path.GetFullPath(command.output), text, new System.Text.UTF8Encoding(false));
                json.Value("output", Path.GetFullPath(command.output));
            }
            FlexibleSpecService.WriteSpec(json, prefabPath, command.profile, warnings);
        }

        private static void ApplySpec(FlexibleCommand command, FlexibleJson json, List<string> warnings, bool inEditor)
        {
            if (string.IsNullOrEmpty(command.spec) || !File.Exists(command.spec))
                throw new FlexibleCommandException("BAD_REQUEST", $"Spec file not found: '{command.spec}'");

            FlexibleSpec spec;
            try
            {
                spec = JsonUtility.FromJson<FlexibleSpec>(File.ReadAllText(command.spec));
            }
            catch (Exception e)
            {
                throw new FlexibleCommandException("SPEC_INVALID", "Spec is not valid JSON: " + e.Message);
            }

            string prefabPath = ResolvePrefab(!string.IsNullOrEmpty(command.prefab) ? command.prefab : spec?.prefab);
            EnsureWritable(prefabPath, inEditor);

            List<FlexibleCommitReport> reports = FlexibleSpecService.Apply(prefabPath, spec, command.dryRun);
            json.Value("prefab", prefabPath).Value("dryRun", command.dryRun).BeginArray("profiles");
            foreach (FlexibleCommitReport report in reports)
            {
                json.BeginObject().Value("profile", report.profileId).Value("saved", report.success && !report.dryRun)
                    .Value("recorded", report.recorded).BeginArray("changes");
                foreach (FlexibleChange change in report.changes)
                {
                    json.BeginObject().Value("path", change.path).Value("kind", change.kind);
                    if (!string.IsNullOrEmpty(change.role))
                        json.Value("role", change.role);
                    json.EndObject();
                }
                json.EndArray().EndObject();
                warnings.AddRange(report.warnings.Select(w => $"{report.profileId}: {w}"));
            }
            json.EndArray();
        }

        private static void SetRole(FlexibleCommand command, FlexibleJson json, List<string> warnings, bool inEditor)
        {
            string prefabPath = ResolvePrefab(command.prefab);
            EnsureWritable(prefabPath, inEditor);
            if (string.IsNullOrEmpty(command.path))
                throw new FlexibleCommandException("BAD_REQUEST", "set-role needs -Path.");
            if (command.role == null)
                throw new FlexibleCommandException("BAD_REQUEST", "set-role needs -Role (use -Role '' to clear the role).");

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                if (!FlexiblePathUtility.BuildMap((RectTransform)root.transform).TryGetValue(command.path, out RectTransform target))
                    throw new FlexibleCommandException("TARGET_NOT_FOUND", $"Path not found: '{command.path}'");

                var layout = root.GetComponent<FlexibleLayout>();
                if (layout == null)
                {
                    layout = root.AddComponent<FlexibleLayout>();
                    layout.Settings = FlexibleSettingsUtility.GetOrCreateDefault();
                    warnings.Add("FlexibleLayout was added to the prefab root.");
                }

                FlexibleProfileSettings settings = layout.Settings != null ? layout.Settings : FlexibleSettingsUtility.GetOrCreateDefault();
                if (!string.IsNullOrEmpty(command.role) && !settings.Roles.Contains(command.role))
                    warnings.Add($"Role '{command.role}' is not in the settings role list ({string.Join(", ", settings.Roles)}).");

                layout.SetRole(target, command.role);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            json.Value("prefab", prefabPath).Value("path", command.path).Value("role", command.role ?? "");
        }

        // 연습용 사본 등: 원본을 그대로 복제 (프로필 데이터·역할 포함)
        private static void Duplicate(FlexibleCommand command, FlexibleJson json, List<string> warnings, bool inEditor)
        {
            string source = ResolvePrefab(command.prefab);
            // 실행 스크립트는 -Out 을 절대 경로로 넘기므로 프로젝트 안이면 애셋 경로로 되돌린다
            string target = (command.output ?? string.Empty).Replace('\\', '/');
            string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/') + "/";
            if (target.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                target = target.Substring(projectRoot.Length);
            // 이름만 주면 원본 옆에 만든다
            if (!target.EndsWith(".prefab") && Path.GetFileName(target).Length > 0)
                target = Path.GetDirectoryName(source)?.Replace('\\', '/') + "/" + Path.GetFileName(target) + ".prefab";
            if (!target.StartsWith("Assets/") || !target.EndsWith(".prefab"))
                throw new FlexibleCommandException("BAD_REQUEST", "duplicate needs -Out <Name> or -Out Assets/.../Name.prefab");
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(target) != null)
                throw new FlexibleCommandException("TARGET_EXISTS", $"'{target}' already exists. Choose another name or reuse it.");

            FlexibleSettingsUtility.EnsureFolder(Path.GetDirectoryName(target)?.Replace('\\', '/'));
            if (!AssetDatabase.CopyAsset(source, target))
                throw new FlexibleCommandException("INTERNAL", $"Could not copy '{source}' to '{target}'.");
            json.Value("source", source).Value("prefab", target);
        }

        // 한 프로필의 기록을 모두 지워 그 프로필이 기준(세로) 배치를 따르게 한다
        private static void ClearProfile(FlexibleCommand command, FlexibleJson json, List<string> warnings, bool inEditor)
        {
            string prefabPath = ResolvePrefab(command.prefab);
            EnsureWritable(prefabPath, inEditor);
            FlexibleProfileSettings settings = GetSettings(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            FlexibleProfile profile = SelectProfiles(settings, command.profile).Single();
            if (string.IsNullOrEmpty(command.profile))
                throw new FlexibleCommandException("BAD_REQUEST", "clear-profile needs -Profile.");
            if (profile.id == settings.BaseProfileId)
                throw new FlexibleCommandException("BASE_PROFILE", "The base profile is the prefab itself and has no records to clear.");
            if (FlexibleWorkspaceService.WorkspaceExists(prefabPath, profile.id))
                throw new FlexibleCommandException("WORKSPACE_EXISTS", $"A GUI workspace for '{profile.id}' is pending. Commit or cancel it first.");

            int removed = 0;
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var layout = root.GetComponent<FlexibleLayout>();
                if (layout != null)
                {
                    foreach (FlexibleLayoutEntry entry in layout.Entries)
                        removed += entry.states.RemoveAll(s => s.profileId == profile.id);
                    layout.Entries.RemoveAll(e => e.states.Count == 0);
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            json.Value("prefab", prefabPath).Value("profile", profile.id).Value("removed", removed);
        }

        // 튜토리얼 연습 사본과 그 사본 때문에 생긴 것(작업 프리팹·테스트 배치·스펙·렌더 이미지)을 지운다. 프리팹은 휴지통으로 보낸다
        private static void CleanupTutorial(FlexibleCommand command, FlexibleJson json, List<string> warnings, bool inEditor)
        {
            string prefabPath = ResolvePrefab(command.prefab);
            string name = Path.GetFileNameWithoutExtension(prefabPath);
            string samples = SampleFolder;
            if (samples == null || Path.GetDirectoryName(prefabPath)?.Replace('\\', '/') != samples || name == SamplePrefabName || name.IndexOf("Tutorial", StringComparison.OrdinalIgnoreCase) < 0)
                throw new FlexibleCommandException("NOT_PRACTICE_COPY", $"'{prefabPath}' is not a tutorial practice copy (*Tutorial*.prefab next to {SamplePrefabName}). Only practice copies can be cleaned up.");
            EnsureWritable(prefabPath, inEditor);
            string guid = AssetDatabase.AssetPathToGUID(prefabPath);

            // 열린 씬의 테스트 배치 중 이 프리팹 것만 지운다
            int instances = 0;
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                List<GameObject> found = scene.GetRootGameObjects()
                    .SelectMany(go => go.GetComponentsInChildren<Transform>(true))
                    .Where(t => t.name.StartsWith(TestInstancePrefix) && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) == prefabPath)
                    .Select(t => t.gameObject).ToList();
                foreach (GameObject go in found)
                    UnityEngine.Object.DestroyImmediate(go);
                if (found.Count > 0)
                {
                    instances += found.Count;
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            if (DefaultTestScene != null && File.Exists(DefaultTestScene) && !EditorSceneManager.GetSceneByPath(DefaultTestScene).isLoaded && File.ReadAllText(DefaultTestScene).Contains(guid))
                warnings.Add($"'{DefaultTestScene}' still has a test instance of this prefab. Run scene-place then scene-clear, or remove it in Unity.");

            var workspaces = new List<string>();
            FlexibleProfileSettings settings = GetSettings(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            foreach (FlexibleProfile profile in settings.Profiles)
            {
                string workspacePath = FlexibleWorkspaceService.GetWorkspacePath(guid, profile.id);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(workspacePath) != null && AssetDatabase.DeleteAsset(workspacePath))
                    workspaces.Add(workspacePath);
            }

            var files = new List<string>();
            DeleteFiles(Path.Combine(FlexibleCommandBridge.WorkFolder, "specs"), f => f.StartsWith(name + "_") || f.StartsWith(name + "."), files);
            DeleteFiles(Path.Combine(FlexibleCommandBridge.WorkFolder, "renders"), f => f.StartsWith(name + "__") || f.StartsWith("gameview__"), files);

            if (!AssetDatabase.MoveAssetToTrash(prefabPath))
                throw new FlexibleCommandException("INTERNAL", $"Could not move '{prefabPath}' to the trash.");

            json.Value("prefab", prefabPath).Value("trashed", true).Value("testInstances", instances)
                .Strings("workspaces", workspaces).Value("files", files.Count);
        }

        private static void DeleteFiles(string folder, Func<string, bool> match, List<string> deleted)
        {
            if (!Directory.Exists(folder))
                return;
            foreach (string file in Directory.GetFiles(folder))
            {
                if (!match(Path.GetFileName(file)))
                    continue;
                File.Delete(file);
                deleted.Add(file);
            }
        }

        // 튜토리얼 샘플(FlexibleSample, SampleScene, 기본 설정)을 Assets/FlexibleUITool/Samples 로 가져온다
        private static void ImportSamples(FlexibleCommand command, FlexibleJson json, List<string> warnings, bool inEditor)
        {
            string folder = FlexibleSettingsUtility.ImportTutorialSample(out bool imported);
            if (folder == null)
                throw new FlexibleCommandException("SAMPLE_NOT_FOUND", $"{SamplePrefabName} is missing and could not be imported. Restore the tool's Samples folder, or reinstall the package.");
            json.Value("imported", imported).Value("folder", folder);
        }

        // 테스트 씬을 열고 캔버스 아래에 프리팹을 부모에 맞춰 놓은 뒤 저장한다 (이전 테스트 배치는 교체)
        private static void ScenePlace(FlexibleCommand command, FlexibleJson json, List<string> warnings, bool inEditor)
        {
            string prefabPath = ResolvePrefab(command.prefab);
            UnityEngine.SceneManagement.Scene scene = OpenTestScene(command, warnings, out string previous);

            Canvas canvas = scene.GetRootGameObjects()
                .SelectMany(go => go.GetComponentsInChildren<Canvas>(true))
                .Where(c => c.isRootCanvas)
                .OrderByDescending(c => c.GetComponent<FlexibleCanvasScaler>() != null)
                .FirstOrDefault();
            if (canvas == null)
                throw new FlexibleCommandException("CANVAS_NOT_FOUND", $"No root Canvas in '{scene.path}'.");
            if (canvas.GetComponent<FlexibleCanvasScaler>() == null)
                warnings.Add("The canvas has no FlexibleCanvasScaler; UI size will change with orientation.");

            int removed = RemoveTestInstances(canvas.transform);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), scene);
            instance.name = TestInstancePrefix + instance.name;
            var rt = (RectTransform)instance.transform;
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f);
            rt.localScale = Vector3.one;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            json.Value("scene", scene.path).Value("canvas", canvas.name).Value("instance", instance.name)
                .Value("replaced", removed).Value("previousScene", previous ?? "");
        }

        // 테스트 배치를 지우고 저장한 뒤, scene-place 전에 열려 있던 씬으로 돌아간다
        private static void SceneClear(FlexibleCommand command, FlexibleJson json, List<string> warnings, bool inEditor)
        {
            string scenePath = string.IsNullOrEmpty(command.scene) ? DefaultTestScene : command.scene;
            if (string.IsNullOrEmpty(scenePath))
                throw new FlexibleCommandException("SCENE_NOT_FOUND", "No test scene. Run import-samples or pass -Scene.");
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetSceneByPath(scenePath);
            int removed = 0;
            if (scene.IsValid() && scene.isLoaded)
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true).Where(c => c.isRootCanvas))
                        removed += RemoveTestInstances(canvas.transform);
                }
                if (removed > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            else
            {
                warnings.Add($"'{scenePath}' is not open; nothing was removed.");
            }

            string previous = SessionState.GetString(PreviousSceneKey, "");
            SessionState.EraseString(PreviousSceneKey);
            bool reopened = false;
            if (!string.IsNullOrEmpty(previous) && previous != scenePath && File.Exists(previous))
            {
                EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                reopened = true;
            }
            json.Value("scene", scenePath).Value("removed", removed).Value("reopened", reopened ? previous : "");
        }

        // Unity에서 Flexible UI 창의 검수 탭을 열고 검수를 실행한다 (사람이 썸네일 표로 보게). 결과 요약도 돌려준다
        private static void Review(FlexibleCommand command, FlexibleJson json, List<string> warnings, bool inEditor)
        {
            if (!inEditor)
                throw new FlexibleCommandException("BAD_REQUEST", "review needs the running editor.");

            string prefabPath = string.IsNullOrEmpty(command.prefab) ? null : ResolvePrefab(command.prefab);
            if (string.IsNullOrEmpty(prefabPath) && !string.IsNullOrEmpty(command.folder) && !AssetDatabase.IsValidFolder(command.folder))
                throw new FlexibleCommandException("BAD_REQUEST", $"Folder '{command.folder}' not found.");

            FlexibleReviewPanel window = FlexibleWindow.OpenAndReview(command.folder, prefabPath);
            int total = 0;
            var prefabs = new HashSet<string>();
            json.BeginArray("results");
            foreach (var (prefab, screen, profile, issues) in window.Results)
            {
                prefabs.Add(prefab);
                total += issues.Count;
                if (issues.Count == 0)
                    continue;
                json.BeginObject().Value("prefab", prefab).Value("target", screen).Value("profile", profile).Strings("issues", issues).EndObject();
            }
            json.EndArray().Value("prefabCount", prefabs.Count).Value("issueCount", total);
        }

        // 에디터 모드에서 Game 뷰 해상도를 바꾼다. 플레이 중에는 브리지가 FlexibleGameViewShots로 차례 전환·캡처한다
        private static void GameView(FlexibleCommand command, FlexibleJson json, List<string> warnings, bool inEditor)
        {
            if (!inEditor)
                throw new FlexibleCommandException("BAD_REQUEST", "game-view needs the running editor.");

            List<string> keys = GetGameViewKeys(command);
            if (keys.Count > 1)
                warnings.Add("Not in Play Mode: only the last preset was selected and nothing was captured. Press Play to capture every preset.");
            Vector2Int resolution = SelectGameView(keys[keys.Count - 1], out _);
            json.Numbers("resolution", resolution.x, resolution.y).Value("playing", false);
        }

        public static List<string> GetGameViewKeys(FlexibleCommand command)
        {
            var keys = new List<string>(command.presets ?? Array.Empty<string>());
            if (keys.Count == 0 && !string.IsNullOrEmpty(command.profile))
                keys.Add(command.profile);
            if (keys.Count == 0)
                throw new FlexibleCommandException("BAD_REQUEST", "game-view needs -Preset <index|name|profile id> (several: comma-separated).");
            return keys;
        }

        // 프로필 id면 작업 해상도, 아니면 기기 프리셋 해상도로 Game 뷰를 맞춘다
        public static Vector2Int SelectGameView(string key, out string name)
        {
            FlexibleProfileSettings settings = FlexibleSettingsUtility.GetOrCreateDefault();
            Vector2 resolution;
            FlexibleProfile profile = settings.Find(key);
            if (profile != null)
            {
                resolution = profile.workResolution;
                name = profile.id;
            }
            else
            {
                FlexiblePreviewPreset preset = int.TryParse(key, out int index) && index >= 0 && index < settings.PreviewPresets.Count
                    ? settings.PreviewPresets[index]
                    : settings.PreviewPresets.FirstOrDefault(p => p.name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
                if (preset == null)
                    throw new FlexibleCommandException("PRESET_NOT_FOUND", $"No preset or profile matches '{key}'.");
                resolution = preset.resolution;
                name = preset.name;
            }

            int width = Mathf.RoundToInt(resolution.x);
            int height = Mathf.RoundToInt(resolution.y);
            if (!FlexibleGameViewUtility.TrySelectResolution(width, height, $"Flexible {name} {width}x{height}"))
                throw new FlexibleCommandException("GAME_VIEW_FAILED", "Could not change the Game view resolution. Open the Game view and retry.");
            return new Vector2Int(width, height);
        }

        // 플레이 중 씬의 FlexibleLayout이 지금 어느 프로필인지
        public static void AppendRuntimeProfiles(FlexibleJson json)
        {
            json.BeginArray("layouts");
            foreach (FlexibleLayout layout in UnityEngine.Object.FindObjectsByType<FlexibleLayout>(FindObjectsSortMode.None))
            {
                Rect rect = ((RectTransform)layout.transform).rect;
                json.BeginObject().Value("object", layout.name).Value("profile", layout.CurrentProfile?.id ?? "")
                    .Numbers("size", Round(rect.width), Round(rect.height)).EndObject();
            }
            json.EndArray();
        }

        private static UnityEngine.SceneManagement.Scene OpenTestScene(FlexibleCommand command, List<string> warnings, out string previous)
        {
            string scenePath = string.IsNullOrEmpty(command.scene) ? DefaultTestScene : command.scene;
            if (string.IsNullOrEmpty(scenePath))
                throw new FlexibleCommandException("SCENE_NOT_FOUND", "No test scene. Run import-samples or pass -Scene.");
            if (!File.Exists(scenePath))
                throw new FlexibleCommandException("SCENE_NOT_FOUND", $"Scene '{scenePath}' not found.");
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new FlexibleCommandException("PREFAB_OPEN", "Prefab Mode is open. Close it and retry.");

            previous = null;
            UnityEngine.SceneManagement.Scene active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (active.path == scenePath)
                return active;

            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene open = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (open.isDirty)
                    throw new FlexibleCommandException("SCENE_UNSAVED", $"Scene '{(string.IsNullOrEmpty(open.path) ? open.name : open.path)}' has unsaved changes. Save it and retry.");
            }

            previous = active.path;
            if (!string.IsNullOrEmpty(previous))
                SessionState.SetString(PreviousSceneKey, previous);
            return EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }

        private static int RemoveTestInstances(Transform canvas)
        {
            List<GameObject> old = canvas.Cast<Transform>().Where(t => t.name.StartsWith(TestInstancePrefix)).Select(t => t.gameObject).ToList();
            foreach (GameObject go in old)
                UnityEngine.Object.DestroyImmediate(go);
            return old.Count;
        }

        // ───────── 공통 ─────────

        private static void WriteIssues(FlexibleJson json, List<FlexibleIssue> issues)
        {
            json.BeginArray("issues");
            foreach (FlexibleIssue issue in issues)
                json.BeginObject().Value("code", issue.code).Strings("blocks", issue.blocks).EndObject();
            json.EndArray();
        }

        private struct ScreenTarget
        {
            public string name;
            public float aspect;
            public Rect safeArea;
            public int profileIndex; // -1 = 비율로 고른다 (경계 비율은 히스테리시스로 유지되는 프로필을 직접 지정)

            public int ProfileIndex(FlexibleProfileSettings settings)
            {
                return profileIndex >= 0 ? profileIndex : settings.SelectProfile(aspect, -1);
            }
        }

        // -Preset 은 번호·이름(부분 일치)·프로필 id·edges(프로필 경계 비율), -Aspect 는 임의 비율.
        // 없으면 render는 프로필별 작업 해상도, check는 모든 프리셋 + 프로필 경계 비율
        private static List<ScreenTarget> ResolveTargets(FlexibleProfileSettings settings, FlexibleCommand command, bool defaultToPresets = false)
        {
            var targets = new List<ScreenTarget>();
            string[] keys = command.presets ?? Array.Empty<string>();
            if (keys.Length == 0 && !string.IsNullOrEmpty(command.profile) && (command.aspects == null || command.aspects.Length == 0))
                keys = new[] { command.profile };

            foreach (string text in command.aspects ?? Array.Empty<string>())
            {
                if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float aspect) || aspect < 0.2f || aspect > 5f)
                    throw new FlexibleCommandException("BAD_REQUEST", $"-Aspect '{text}' is not a width/height ratio between 0.2 and 5 (e.g. 1.3).");
                int index = settings.SelectProfile(aspect, -1);
                targets.Add(new ScreenTarget { name = $"aspect@{aspect:0.00}", aspect = aspect, safeArea = settings.GetSafeArea(index), profileIndex = -1 });
            }

            if (keys.Length == 0)
            {
                if (targets.Count > 0)
                    return targets;
                if (defaultToPresets)
                {
                    targets.AddRange(settings.PreviewPresets.Select(FromPreset));
                    targets.AddRange(EdgeTargets(settings));
                }
                else
                {
                    targets.AddRange(settings.Profiles.Select(p => FromProfile(p, settings)));
                }
                return targets;
            }

            foreach (string key in keys)
            {
                if (string.Equals(key, "edges", StringComparison.OrdinalIgnoreCase))
                {
                    targets.AddRange(EdgeTargets(settings));
                    continue;
                }

                FlexibleProfile profile = settings.Find(key);
                if (profile != null)
                {
                    targets.Add(FromProfile(profile, settings));
                    continue;
                }

                FlexiblePreviewPreset preset = int.TryParse(key, out int index) && index >= 0 && index < settings.PreviewPresets.Count
                    ? settings.PreviewPresets[index]
                    : settings.PreviewPresets.FirstOrDefault(p => p.name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
                if (preset == null)
                    throw new FlexibleCommandException("PRESET_NOT_FOUND", $"No preset or profile matches '{key}'. Use 'profiles' to list them.");
                targets.Add(FromPreset(preset));
            }
            return targets;
        }

        private static ScreenTarget FromPreset(FlexiblePreviewPreset preset)
        {
            return new ScreenTarget { name = preset.name, aspect = preset.Aspect, safeArea = preset.NormalizedSafeArea, profileIndex = -1 };
        }

        private static ScreenTarget FromProfile(FlexibleProfile profile, FlexibleProfileSettings settings)
        {
            Vector2 size = FlexibleSpecService.GetWorkSize(profile, settings);
            return new ScreenTarget { name = profile.id, aspect = size.x / size.y, safeArea = new Rect(0f, 0f, 1f, 1f), profileIndex = settings.IndexOf(profile.id) };
        }

        // 프로필마다 화면에 나올 수 있는 가장 좁은·넓은 비율 (고정 크기 블록은 여기서 넘치기 쉽다). 이름 예: landscape@1.24
        private static IEnumerable<ScreenTarget> EdgeTargets(FlexibleProfileSettings settings)
        {
            for (int i = 0; i < settings.Profiles.Count; i++)
            {
                foreach (float aspect in settings.GetEdgeAspects(i))
                    yield return new ScreenTarget { name = $"{settings.Profiles[i].id}@{aspect:0.00}", aspect = aspect, safeArea = settings.GetSafeArea(i), profileIndex = i };
            }
        }

        private static IEnumerable<FlexibleProfile> SelectProfiles(FlexibleProfileSettings settings, string profileId)
        {
            if (string.IsNullOrEmpty(profileId))
                return settings.Profiles;

            FlexibleProfile profile = settings.Find(profileId);
            if (profile == null)
                throw new FlexibleCommandException("PROFILE_NOT_FOUND", $"Profile '{profileId}' not found. Available: {string.Join(", ", settings.Profiles.Select(p => p.id))}");
            return new[] { profile };
        }

        // 기본: 루트 직속 + 역할 블록 + 프로필 기록이 있는 블록. all이면 하위 전부
        private static List<RectTransform> SelectBlocks(RectTransform root, bool all)
        {
            if (all)
                return FlexiblePathUtility.CollectTargets(root);

            var blocks = new List<RectTransform>();
            foreach (Transform child in root)
            {
                if (child is RectTransform rt)
                    blocks.Add(rt);
            }

            var layout = root.GetComponent<FlexibleLayout>();
            if (layout != null)
            {
                blocks.AddRange(layout.Roles.Where(r => r.target != null).Select(r => r.target));
                blocks.AddRange(layout.Entries.Where(e => e.target != null).Select(e => e.target));
            }
            return blocks.Distinct().ToList();
        }

        private static FlexibleProfileSettings GetSettings(GameObject asset)
        {
            var layout = asset.GetComponent<FlexibleLayout>();
            return layout != null && layout.Settings != null ? layout.Settings : FlexibleSettingsUtility.GetOrCreateDefault();
        }

        // 전체 경로(Assets/...prefab) 또는 파일 이름
        public static string ResolvePrefab(string prefab)
        {
            if (string.IsNullOrEmpty(prefab))
                throw new FlexibleCommandException("BAD_REQUEST", "This command needs -Prefab.");

            string path = prefab.Replace('\\', '/');
            if (path.EndsWith(".prefab") && AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                return Validate(path);

            string name = Path.GetFileNameWithoutExtension(path);
            List<string> matches = AssetDatabase.FindAssets(name + " t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileNameWithoutExtension(p) == name && !p.StartsWith(FlexibleWorkspaceService.WorkspaceFolder))
                .ToList();
            if (matches.Count == 0)
                throw new FlexibleCommandException("PREFAB_NOT_FOUND", $"Prefab '{prefab}' not found.");
            if (matches.Count > 1)
                throw new FlexibleCommandException("AMBIGUOUS_PREFAB", $"Several prefabs are named '{name}': {string.Join(", ", matches)}. Pass the full path.");
            return Validate(matches[0]);
        }

        private static string Validate(string path)
        {
            if (!(AssetDatabase.LoadAssetAtPath<GameObject>(path).transform is RectTransform))
                throw new FlexibleCommandException("NOT_UI_PREFAB", $"'{path}' root is not a RectTransform.");
            return path;
        }

        // 사람이 GUI로 같은 프리팹을 편집 중이면 쓰기 명령을 거절한다
        private static void EnsureWritable(string prefabPath, bool inEditor)
        {
            if (!inEditor)
                return;

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
                return;

            if (stage.assetPath == prefabPath)
                throw new FlexibleCommandException("PREFAB_OPEN", $"'{prefabPath}' is open in Prefab Mode. Close it (save first if needed) and retry.");

            var workspace = stage.prefabContentsRoot.GetComponent<FlexibleWorkspace>();
            if (workspace != null && !workspace.inPlace && AssetDatabase.GUIDToAssetPath(workspace.sourceGuid) == prefabPath)
                throw new FlexibleCommandException("PREFAB_OPEN", $"A workspace of '{prefabPath}' is open in Prefab Mode. Commit or cancel it and retry.");
        }

        private static void WarnIfOpenAndDirty(string prefabPath, bool inEditor, List<string> warnings)
        {
            PrefabStage stage = inEditor ? PrefabStageUtility.GetCurrentPrefabStage() : null;
            if (stage != null && stage.assetPath == prefabPath && stage.scene.isDirty)
                warnings.Add("The prefab is open with unsaved changes; this result reflects the saved asset.");
        }

        private static float Round(float value)
        {
            return Mathf.Round(value * 10f) / 10f;
        }

        private static float Round3(float value)
        {
            return Mathf.Round(value * 1000f) / 1000f;
        }

        public static string Sanitize(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
        }
    }
}
