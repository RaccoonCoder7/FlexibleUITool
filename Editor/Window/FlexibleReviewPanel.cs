using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // 검수 탭: FlexibleLayout이 붙은 프리팹을 기기 프리셋별로 렌더링해 한 화면에서 확인한다
    [System.Serializable]
    public class FlexibleReviewPanel
    {
        // 문구는 그릴 때 현재 언어로 만든다 (언어를 바꿔도 다시 검수할 필요 없게)
        private class Cell
        {
            public string presetName; // 프리셋 이름 (경계 비율이면 null)
            public float aspect;
            public RenderTexture texture;
            public FlexibleProfile profile;
            public List<FlexibleIssue> issues;

            public string ScreenName => presetName != null ? T(presetName) : F("Edge {0:0.00}", aspect);
            // CLI에 넘기는 이름 (check·render의 -Preset 이 그대로 받는 이름)
            public string CliName => presetName ?? $"{profile.id}@{aspect:0.00}";
            public List<string> Messages => issues.ConvertAll(i => i.Message);
        }

        private class Row
        {
            public string path;
            public readonly List<Cell> cells = new List<Cell>();
        }

        [SerializeField] private DefaultAsset folder;
        [SerializeField] private float thumbnailHeight = 200f;
        // 기본은 문제 있는 화면만 보여 준다 (튜토리얼은 모든 화면을 보여 준다)
        [SerializeField] private bool showAllScreens;
        private bool OnlyIssues => !showAllScreens && !FlexibleTutorial.Active;
        // 기준(세로) 화면의 문제는 원래 디자인 문제라 기본으로 숨긴다 (새로 만든 화면의 문제에 집중)
        [SerializeField] private bool hideBaseIssues = true;
        [SerializeField] private bool includeEdges = true;
        // 검수 범위: 열린 프리팹 / 열린 씬에 놓인 프리팹 / 폴더
        private enum Scope { OpenPrefab, Scene, Folder }
        [SerializeField] private Scope scope = Scope.OpenPrefab;
        private float renderedHeight; // 마지막 검수 때 썸네일을 그린 높이 (화면 단위)
        private bool[] presetEnabled;
        private string targetPrefab; // 지정되면 이 프리팹만 검수 (CLI review -Prefab)
        private Vector2 scroll;
        private readonly List<Row> rows = new List<Row>();
        // 「열린 프리팹만」으로 검수한 프리팹 (다른 프리팹을 열면 그 결과는 지운다)
        private string checkedOpen;

        // 「문제 보기」 뒤 편집 탭으로 옮긴다 (창이 넣어 준다)
        [System.NonSerialized] public System.Action ShowEditTab;

        // 검수를 돌린 횟수 (튜토리얼 확인용)
        public int RunCount { get; private set; }

        public void OnDisable()
        {
            ReleaseRows();
        }

        // 검수 결과 요약 (프리팹, 화면 이름, 프로필 id, 창에 보이는 문제 문장, 기준 화면 문제라 숨긴 수). 창의 건수와 같은 기준으로 센다
        public IEnumerable<(string prefab, string screen, string profile, List<string> issues, int hidden)> Results =>
            rows.SelectMany(r => r.cells.Select(c =>
            {
                List<FlexibleIssue> visible = Visible(c);
                return (r.path, c.CliName, c.profile.id, visible.ConvertAll(i => i.Message), c.issues.Count - visible.Count);
            }));

        public bool HidesBaseIssues => hideBaseIssues;

        // 바로 검수한다 (CLI review). 프리팹을 주면 그것만, 아니면 폴더(비우면 Assets 전체)
        public void RunFor(string folderPath, string prefabPath)
        {
            folder = string.IsNullOrEmpty(folderPath) ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
            targetPrefab = string.IsNullOrEmpty(prefabPath) ? null : prefabPath;
            Scope previous = scope;
            scope = Scope.Folder;
            Run(FlexibleSettingsUtility.GetOrCreateDefault());
            scope = previous;
        }

        private void EnsurePresetFlags(FlexibleProfileSettings settings)
        {
            if (presetEnabled == null || presetEnabled.Length != settings.PreviewPresets.Count)
                presetEnabled = Enumerable.Repeat(true, settings.PreviewPresets.Count).ToArray();
        }

        public void Draw()
        {
            FlexibleProfileSettings settings = FlexibleSettingsUtility.GetOrCreateDefault();
            EnsurePresetFlags(settings);
            string open = OpenPrefabPath();
            // 지난번에 검수한 것과 다른 프리팹이 열려 있으면 그 결과는 이 프리팹의 것이 아니다
            if (checkedOpen != null && open != null && open != checkedOpen)
            {
                ReleaseRows();
                checkedOpen = null;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (!string.IsNullOrEmpty(targetPrefab))
                {
                    GUILayout.Label(F("Target: {0}", Path.GetFileNameWithoutExtension(targetPrefab)), GUILayout.ExpandWidth(false));
                    if (GUILayout.Button(T("All"), GUILayout.Width(48f)))
                        targetPrefab = null;
                }
                else
                {
                    using (new EditorGUI.DisabledScope(open == null))
                    {
                        if (Toggle(scope == Scope.OpenPrefab && open != null, new GUIContent(T("Only the open prefab")), EditorStyles.miniButtonLeft, GUILayout.ExpandWidth(false)))
                            scope = Scope.OpenPrefab;
                    }
                    if (Toggle(scope == Scope.Scene || (scope == Scope.OpenPrefab && open == null), new GUIContent(T("This scene"), T("Prefabs placed in the open scene (nested ones too).")), EditorStyles.miniButtonMid, GUILayout.ExpandWidth(false)))
                        scope = Scope.Scene;
                    if (Toggle(scope == Scope.Folder, new GUIContent(T("Folder")), EditorStyles.miniButtonRight, GUILayout.ExpandWidth(false)))
                        scope = Scope.Folder;
                    if (scope == Scope.Folder)
                        folder = (DefaultAsset)EditorGUILayout.ObjectField(folder, typeof(DefaultAsset), false, GUILayout.Width(160f));
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent(T("Run check"), T("Finds problems on many device screens.")), GUILayout.Width(100f), GUILayout.Height(22f)))
                {
                    if (scope == Scope.OpenPrefab && open != null && string.IsNullOrEmpty(targetPrefab))
                    {
                        UnityEditor.SceneManagement.PrefabStage stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                        if (stage != null && stage.scene.isDirty)
                            FlexibleScreenSession.Save(stage);
                        targetPrefab = open;
                        Run(settings);
                        targetPrefab = null;
                        checkedOpen = open;
                    }
                    else
                    {
                        Run(settings);
                    }
                    GUIUtility.ExitGUI();
                }
                FlexibleGui.Mark("check:run");
            }

            float available = EditorGUIUtility.currentViewWidth - 24f;
            int index = 0;
            while (index < settings.PreviewPresets.Count)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    float used = 0f;
                    do
                    {
                        var content = new GUIContent(T(settings.PreviewPresets[index].name));
                        float width = EditorStyles.miniButton.CalcSize(content).x + 4f;
                        if (used > 0f && used + width > available)
                            break;
                        presetEnabled[index] = Toggle(presetEnabled[index], content, EditorStyles.miniButton, GUILayout.Width(width));
                        used += width;
                        index++;
                    } while (index < settings.PreviewPresets.Count);
                    GUILayout.FlexibleSpace();
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                includeEdges = Toggle(includeEdges, new GUIContent(T("Screen edge ratios"), T("Also checks the narrowest and widest ratio where each screen can appear. Fixed-size objects overflow there first.")), EditorStyles.miniButton);
                showAllScreens = !Toggle(!showAllScreens, new GUIContent(T("Only issues")), EditorStyles.miniButton);
                hideBaseIssues = Toggle(hideBaseIssues, new GUIContent(T("Hide base screen issues"), T("Issues on the base (portrait) screen come from the original design. Hide them to focus on the screens you made.")), EditorStyles.miniButton);
                GUILayout.Label(T("Thumbnail"), GUILayout.ExpandWidth(false));
                thumbnailHeight = GUILayout.HorizontalSlider(thumbnailHeight, 100f, 400f, GUILayout.Width(100f));
                GUILayout.FlexibleSpace();
            }

            if (rows.Count == 0)
            {
                EditorGUILayout.HelpBox(F("Press [{0}].", T("Run check")), MessageType.Info);
                return;
            }

            if (thumbnailHeight > renderedHeight * 1.1f)
                EditorGUILayout.HelpBox(F("Thumbnails were rendered smaller. Press [{0}] again for sharp thumbnails at this size.", T("Run check")), MessageType.None);

            int total = rows.Sum(r => r.cells.Sum(c => Visible(c).Count));
            EditorGUILayout.LabelField(F("{0} prefabs · {1} issues", rows.Count, total), EditorStyles.boldLabel);
            // 종류별 개수 요약
            var byCode = rows.SelectMany(r => r.cells.SelectMany(Visible)).GroupBy(i => i.code).Select(g => $"{FlexibleIssue.Title(g.Key)} {g.Count()}").ToList();
            if (byCode.Count > 0)
                GUILayout.Label(string.Join(" · ", byCode), EditorStyles.miniLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (Row row in rows)
            {
                // 문제가 하나도 없으면 프리팹 줄은 남겨 「문제 없음」이 보이게 한다
                if (OnlyIssues && total > 0 && row.cells.All(c => Visible(c).Count == 0))
                    continue;
                DrawRow(row);
            }
            EditorGUILayout.EndScrollView();
        }

        // 켜고 끄는 버튼. 꺼진 것은 흐리게 그려 켜진 것과 바로 구분되게 한다
        private static bool Toggle(bool on, GUIContent content, GUIStyle style, params GUILayoutOption[] options)
        {
            Color previous = GUI.color;
            if (!on)
                GUI.color = new Color(previous.r, previous.g, previous.b, previous.a * 0.45f);
            bool result = GUILayout.Toggle(on, content, style, options);
            GUI.color = previous;
            return result;
        }

        private static string OpenPrefabPath()
        {
            UnityEditor.SceneManagement.PrefabStage stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            return FlexibleScreenSession.IsReady(stage, out _) ? stage.assetPath : null;
        }

        private void DrawRow(Row row)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(Path.GetFileNameWithoutExtension(row.path), EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(T("Open"), GUILayout.Width(60f)))
                    {
                        if (FlexibleScreenSession.PrepareToOpen(row.path))
                            UnityEditor.SceneManagement.PrefabStageUtility.OpenPrefab(row.path);
                        GUIUtility.ExitGUI();
                    }
                }
                GUILayout.Label(Summarize(row, FlexibleSettingsUtility.GetOrCreateDefault()), EditorStyles.wordWrappedMiniLabel);

                // 창 폭에 맞춰 줄을 바꾼다 (옆으로 스크롤하지 않게)
                List<Cell> cells = row.cells.Where(c => !OnlyIssues || Visible(c).Count > 0).ToList();
                float available = Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 44f);
                int index = 0;
                while (index < cells.Count)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        float used = 0f;
                        do
                        {
                            float column = CellWidth(cells[index], available);
                            if (used > 0f && used + column + CellGap > available)
                                break;
                            DrawCell(row, cells[index], column);
                            used += column + CellGap;
                            index++;
                        } while (index < cells.Count);
                        GUILayout.FlexibleSpace();
                    }
                }
            }
        }

        private const float CellGap = 6f;

        private float CellWidth(Cell cell, float available)
        {
            float width = thumbnailHeight * cell.texture.width / cell.texture.height;
            return Mathf.Min(Mathf.Max(width, 120f), available);
        }

        private void DrawCell(Row row, Cell cell, float columnWidth)
        {
            float width = Mathf.Min(thumbnailHeight * cell.texture.width / cell.texture.height, columnWidth);
            float height = width * cell.texture.height / cell.texture.width;
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(columnWidth)))
            {
                GUILayout.Label($"{cell.ScreenName}\n→ {FlexibleEditPanel.Name(cell.profile)}", EditorStyles.miniLabel);
                Rect rect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
                GUI.DrawTexture(rect, cell.texture, ScaleMode.StretchToFill, false);
                List<FlexibleIssue> visible = Visible(cell);
                // 약한 경고(노치 밑 글자)만 있으면 노란색으로
                bool strong = visible.Exists(i => !FlexibleIssue.IsWeak(i.code));
                Color mark = strong ? new Color(1f, 0.4f, 0.3f) : new Color(0.95f, 0.8f, 0.3f);
                if (visible.Count > 0)
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 3f), mark);

                var style = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = visible.Count > 0 ? (strong ? new Color(1f, 0.55f, 0.45f) : mark) : Color.gray } };
                // 같은 종류는 한 줄로 묶는다: 「종류 ×개수: 블록, 블록, …」
                List<string> lines = visible.GroupBy(i => i.code).Select(g =>
                {
                    List<string> names = g.Select(i => i.BlockNames).Distinct().ToList();
                    return $"{FlexibleIssue.Title(g.Key)} ×{g.Count()}: {string.Join(", ", names.Take(3))}{(names.Count > 3 ? ", …" : "")}";
                }).ToList();
                if (visible.Count == 0 && cell.issues.Count > 0)
                {
                    if (GUILayout.Button(new GUIContent(F("Base screen issues hidden ({0})", cell.issues.Count), T("Click to show them.")), EditorStyles.linkLabel, GUILayout.Width(columnWidth)))
                        hideBaseIssues = false;
                }
                else
                {
                    string text = visible.Count > 0 ? string.Join("\n", lines) : T("No issues");
                    GUILayout.Label(new GUIContent(text, string.Join("\n", visible.Select(i => i.Message))), style, GUILayout.Width(columnWidth));
                }
                if (visible.Count > 0 && GUILayout.Button(new GUIContent(T("Show the problem"), T("Opens the prefab on this screen in the Edit tab and selects the objects with the problem.")), EditorStyles.miniButton, GUILayout.Width(columnWidth)))
                {
                    if (ShowProblem(row.path, cell.profile.id, visible))
                    {
                        FlexibleEditPanel.SetProblem(row.path, cell.profile.id, cell.ScreenName, visible);
                        ShowEditTab?.Invoke();
                    }
                    GUIUtility.ExitGUI();
                }
            }
        }

        // 문제가 난 화면으로 프리팹을 열고 그 블록을 골라 둔다 (열고 나서 직접 찾지 않아도 되게)
        private static bool ShowProblem(string path, string profileId, List<FlexibleIssue> issues)
        {
            string[] blockPaths = issues.Where(i => i.blocks != null).SelectMany(i => i.blocks).Distinct().ToArray();
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == path)
            {
                if (FlexibleScreenSession.GetActiveProfileId(stage) != profileId)
                    FlexibleScreenSession.ShowProfile(stage, profileId);
            }
            else
            {
                if (!FlexibleScreenSession.PrepareToOpen(path))
                    return false;
                FlexibleScreenSession.OpenNextOn(path, profileId);
                PrefabStageUtility.OpenPrefab(path);
            }
            EditorApplication.delayCall += () =>
            {
                PrefabStage opened = PrefabStageUtility.GetCurrentPrefabStage();
                if (opened == null || opened.assetPath != path)
                    return;
                Transform root = opened.prefabContentsRoot.transform;
                Object[] found = blockPaths.Select(b => FlexiblePathUtility.FindByPath(root, b)).Where(t => t != null).Select(t => (Object)t.gameObject).ToArray();
                if (found.Length == 0)
                    return;
                Selection.objects = found;
                EditorGUIUtility.PingObject(found[0]);
            };
            return true;
        }

        // 기준 화면 문제를 숨기면, 다른 화면에서도 기준 화면에 이미 있던 같은 문제(원래 디자인·일부러 밖에 둔 것)는 뺀다
        private List<FlexibleIssue> Visible(Cell cell)
        {
            if (!hideBaseIssues)
                return cell.issues;
            string baseId = FlexibleSettingsUtility.GetOrCreateDefault().BaseProfileId;
            if (cell.profile.id == baseId)
                return new List<FlexibleIssue>();
            Row row = rows.Find(r => r.cells.Contains(cell));
            if (row == null)
                return cell.issues;
            var onBase = new HashSet<string>(row.cells.Where(c => c.profile.id == baseId).SelectMany(c => c.issues).Select(Key));
            return cell.issues.Where(i => FlexibleIssue.AlwaysShown(i.code) || !onBase.Contains(Key(i))).ToList();
        }

        private static string Key(FlexibleIssue issue) => issue.code + "|" + string.Join("|", issue.blocks);

        private void Run(FlexibleProfileSettings settings)
        {
            RunCount++;
            ReleaseRows();
            checkedOpen = null;
            FlexibleEditPanel.ClearProblem();
            EnsurePresetFlags(settings);
            List<string> paths = !string.IsNullOrEmpty(targetPrefab) ? new List<string> { targetPrefab }
                : scope == Scope.Folder ? FindTargets(folder != null ? AssetDatabase.GetAssetPath(folder) : "Assets")
                : FindSceneTargets();
            var screens = settings.PreviewPresets.Where((p, i) => presetEnabled[i])
                .Select(p => (name: (string)p.name, aspect: p.Aspect, safeArea: p.NormalizedSafeArea, profileIndex: settings.SelectProfile(p.Aspect, -1)))
                .ToList();
            if (includeEdges)
            {
                for (int i = 0; i < settings.Profiles.Count; i++)
                    screens.AddRange(settings.GetEdgeAspects(i).Select(a => (name: (string)null, aspect: a, safeArea: settings.GetSafeArea(i), profileIndex: i)));
            }
            float scale = EditorGUIUtility.pixelsPerPoint;
            renderedHeight = thumbnailHeight;

            // 열려 있는 프리팹은 편집용 보기(편집하는 동안만 켜고 끈 것)를 그대로 써서 검수한다
            PrefabStage openStage = PrefabStageUtility.GetCurrentPrefabStage();
            Dictionary<string, bool> openView = openStage != null ? FlexibleScreenSession.GetViewByPath(openStage) : null;
            using (var renderer = new FlexiblePreviewRenderer())
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Flexible UI", paths[i], (float)i / paths.Count))
                        break;

                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                    if (asset == null || asset.transform is not RectTransform)
                        continue;

                    var row = new Row { path = paths[i] };
                    renderer.Dirty = true;
                    foreach (var screen in screens)
                    {
                        FlexibleProfile profile = settings.Profiles[screen.profileIndex];
                        Vector2 logical = FlexiblePreviewRenderer.GetLogicalSize(asset, settings, profile.id, screen.aspect);
                        // GPU 메모리를 아끼려고 지금 보이는 크기로만 그린다 (문제 판정은 논리 크기 기준이라 영향 없음)
                        int height = Mathf.RoundToInt(thumbnailHeight * scale);
                        var pixel = new Vector2Int(Mathf.RoundToInt(height * screen.aspect), height);
                        renderer.Render(asset, profile.id, logical, pixel, screen.safeArea, null, openStage != null && openStage.assetPath == paths[i] ? openView : null);

                        var texture = new RenderTexture(pixel.x, pixel.y, 0, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
                        Graphics.Blit(renderer.Texture, texture);
                        row.cells.Add(new Cell
                        {
                            presetName = screen.name,
                            aspect = screen.aspect,
                            texture = texture,
                            profile = profile,
                            issues = renderer.CollectIssueList(screen.safeArea),
                        });
                    }
                    rows.Add(row);
                }
            }

            EditorUtility.ClearProgressBar();
        }

        // 열린 씬(들)에 놓인 Flexible UI 프리팹 (안쪽 프리팹도). 프리팹 모드 중이면 그 뒤의 씬을 본다
        public static List<string> FindSceneTargets()
        {
            var paths = new HashSet<string>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (FlexibleLayout layout in root.GetComponentsInChildren<FlexibleLayout>(true))
                    {
                        if (!PrefabUtility.IsAnyPrefabInstanceRoot(layout.gameObject))
                            continue;
                        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(layout.gameObject);
                        if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab"))
                            paths.Add(path);
                    }
                }
            }
            return paths.OrderBy(p => p).ToList();
        }

        // 프리팹을 일일이 로드하지 않도록 YAML에서 FlexibleLayout 스크립트 GUID를 찾는다
        public static List<string> FindTargets(string searchFolder)
        {
            string scriptGuid = AssetDatabase.FindAssets("FlexibleLayout t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileName(p) == "FlexibleLayout.cs")
                .Select(AssetDatabase.AssetPathToGUID)
                .FirstOrDefault();
            if (string.IsNullOrEmpty(scriptGuid))
                return new List<string>();

            return AssetDatabase.FindAssets("t:Prefab", new[] { searchFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.StartsWith(FlexibleWorkspaceService.WorkspaceFolder))
                .Where(p => File.ReadAllText(p).Contains(scriptGuid))
                .OrderBy(p => p)
                .ToList();
        }

        // 화면마다 바꾼 오브젝트 수와 (검수한 화면이면) 그 화면의 문제 수
        private string Summarize(Row row, FlexibleProfileSettings settings)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(row.path);
            var layout = asset != null ? asset.GetComponent<FlexibleLayout>() : null;
            if (layout == null)
                return "";

            var parts = new List<string>();
            foreach (FlexibleProfile profile in settings.Profiles)
            {
                if (profile.id == settings.BaseProfileId)
                    continue;
                string part = F("{0}: {1} changed", FlexibleEditPanel.Name(profile), FlexibleScreenSession.CountRecords(layout, profile.id));
                List<Cell> cells = row.cells.Where(c => c.profile.id == profile.id).ToList();
                if (cells.Count > 0)
                {
                    int issues = cells.Sum(c => Visible(c).Count);
                    part += " · " + (issues > 0 ? F("{0} issues", issues) : T("No issues"));
                }
                parts.Add(part);
            }
            return string.Join("   ", parts);
        }

        private void ReleaseRows()
        {
            foreach (Cell cell in rows.SelectMany(r => r.cells))
            {
                if (cell.texture == null)
                    continue;
                cell.texture.Release();
                Object.DestroyImmediate(cell.texture);
            }
            rows.Clear();
        }
    }
}
