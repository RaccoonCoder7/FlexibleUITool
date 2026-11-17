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
            public List<string> Messages => issues.ConvertAll(i => i.Message);
        }

        private class Row
        {
            public string path;
            public readonly List<Cell> cells = new List<Cell>();
        }

        [SerializeField] private DefaultAsset folder;
        [SerializeField] private float thumbnailHeight = 200f;
        [SerializeField] private bool onlyIssues;
        // 기준(세로) 화면의 문제는 원래 디자인 문제라 기본으로 숨긴다 (새로 만든 화면의 문제에 집중)
        [SerializeField] private bool hideBaseIssues = true;
        [SerializeField] private bool includeEdges = true;
        [SerializeField] private bool onlyOpenPrefab = true;
        private float renderedHeight; // 마지막 검수 때 썸네일을 그린 높이 (화면 단위)
        private bool[] presetEnabled;
        private string targetPrefab; // 지정되면 이 프리팹만 검수 (CLI review -Prefab)
        private Vector2 scroll;
        private readonly List<Row> rows = new List<Row>();

        // 검수를 돌린 횟수 (튜토리얼 확인용)
        public int RunCount { get; private set; }

        public void OnDisable()
        {
            ReleaseRows();
        }

        // 검수 결과 요약 (프리팹, 화면 이름, 프로필 id, 문제 문장). 문장은 부르는 시점의 언어
        public IEnumerable<(string prefab, string screen, string profile, List<string> issues)> Results =>
            rows.SelectMany(r => r.cells.Select(c => (r.path, c.ScreenName, c.profile.id, c.Messages)));

        // 바로 검수한다 (CLI review). 프리팹을 주면 그것만, 아니면 폴더(비우면 Assets 전체)
        public void RunFor(string folderPath, string prefabPath)
        {
            folder = string.IsNullOrEmpty(folderPath) ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
            targetPrefab = string.IsNullOrEmpty(prefabPath) ? null : prefabPath;
            onlyIssues = false;
            onlyOpenPrefab = false;
            Run(FlexibleSettingsUtility.GetOrCreateDefault());
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
                        onlyOpenPrefab = GUILayout.Toggle(onlyOpenPrefab && open != null, T("Only the open prefab"), EditorStyles.miniButton, GUILayout.ExpandWidth(false));
                    if (!(onlyOpenPrefab && open != null))
                    {
                        GUILayout.Label(T("Folder"), GUILayout.ExpandWidth(false));
                        folder = (DefaultAsset)EditorGUILayout.ObjectField(folder, typeof(DefaultAsset), false, GUILayout.Width(160f));
                    }
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent(T("Run check"), T("Shows your prefabs on many phones and tablets at once, and finds objects that stick out of the screen, overlap, or sit under the notch. Panels the game opens later, lists, and places other prefabs take (such as a menu bar) are checked too.")), GUILayout.Width(100f), GUILayout.Height(22f)))
                {
                    if (onlyOpenPrefab && open != null && string.IsNullOrEmpty(targetPrefab))
                    {
                        UnityEditor.SceneManagement.PrefabStage stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                        if (stage != null && stage.scene.isDirty)
                            FlexibleScreenSession.Save(stage);
                        targetPrefab = open;
                        Run(settings);
                        targetPrefab = null;
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
                        presetEnabled[index] = GUILayout.Toggle(presetEnabled[index], content, EditorStyles.miniButton, GUILayout.Width(width));
                        used += width;
                        index++;
                    } while (index < settings.PreviewPresets.Count);
                    GUILayout.FlexibleSpace();
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                includeEdges = GUILayout.Toggle(includeEdges, new GUIContent(T("Screen edge ratios"), T("Also checks the narrowest and widest ratio where each screen can appear. Fixed-size objects overflow there first.")), EditorStyles.miniButton);
                onlyIssues = GUILayout.Toggle(onlyIssues, T("Only issues"), EditorStyles.miniButton);
                hideBaseIssues = GUILayout.Toggle(hideBaseIssues, new GUIContent(T("Hide base screen issues"), T("Issues on the base (portrait) screen come from the original design. Hide them to focus on the screens you made.")), EditorStyles.miniButton);
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
                if (onlyIssues && row.cells.All(c => Visible(c).Count == 0))
                    continue;
                DrawRow(row);
            }
            EditorGUILayout.EndScrollView();
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
                    EditorGUILayout.LabelField(Path.GetFileNameWithoutExtension(row.path), EditorStyles.boldLabel, GUILayout.Width(220f));
                    EditorGUILayout.LabelField(Summarize(AssetDatabase.LoadAssetAtPath<GameObject>(row.path), FlexibleSettingsUtility.GetOrCreateDefault()), EditorStyles.miniLabel);
                    if (GUILayout.Button(T("Open"), GUILayout.Width(60f)))
                    {
                        UnityEditor.SceneManagement.PrefabStageUtility.OpenPrefab(row.path);
                        GUIUtility.ExitGUI();
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    foreach (Cell cell in row.cells)
                    {
                        if (!onlyIssues || Visible(cell).Count > 0)
                            DrawCell(row, cell);
                    }
                    GUILayout.FlexibleSpace();
                }
            }
        }

        private void DrawCell(Row row, Cell cell)
        {
            float width = thumbnailHeight * cell.texture.width / cell.texture.height;
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Max(width, 120f))))
            {
                GUILayout.Label($"{cell.ScreenName}\n→ {FlexibleEditPanel.Name(cell.profile)}", EditorStyles.miniLabel);
                Rect rect = GUILayoutUtility.GetRect(width, thumbnailHeight, GUILayout.Width(width), GUILayout.Height(thumbnailHeight));
                GUI.DrawTexture(rect, cell.texture, ScaleMode.StretchToFill, false);
                List<FlexibleIssue> visible = Visible(cell);
                if (visible.Count > 0)
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 3f), new Color(1f, 0.4f, 0.3f));

                var style = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = visible.Count > 0 ? new Color(1f, 0.55f, 0.45f) : Color.gray } };
                // 같은 종류는 한 줄로 묶는다: 「종류 ×개수: 블록, 블록, …」
                List<string> lines = visible.GroupBy(i => i.code).Select(g =>
                {
                    List<string> names = g.Select(i => i.BlockNames).Distinct().ToList();
                    return $"{FlexibleIssue.Title(g.Key)} ×{g.Count()}: {string.Join(", ", names.Take(3))}{(names.Count > 3 ? ", …" : "")}";
                }).ToList();
                string text = visible.Count > 0 ? string.Join("\n", lines)
                    : cell.issues.Count > 0 ? F("Base screen issues hidden ({0})", cell.issues.Count)
                    : T("No issues");
                GUILayout.Label(new GUIContent(text, string.Join("\n", visible.Select(i => i.Message))), style, GUILayout.Width(Mathf.Max(width, 120f)));
                if (visible.Count > 0 && GUILayout.Button(new GUIContent(T("Show the problem"), T("Opens the prefab on this screen and selects the objects with the problem.")), EditorStyles.miniButton, GUILayout.Width(Mathf.Max(width, 120f))))
                {
                    ShowProblem(row.path, cell.profile.id, visible);
                    GUIUtility.ExitGUI();
                }
            }
        }

        // 문제가 난 화면으로 프리팹을 열고 그 블록을 골라 둔다 (열고 나서 직접 찾지 않아도 되게)
        private static void ShowProblem(string path, string profileId, List<FlexibleIssue> issues)
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
            return cell.issues.Where(i => !onBase.Contains(Key(i))).ToList();
        }

        private static string Key(FlexibleIssue issue) => issue.code + "|" + string.Join("|", issue.blocks);

        private void Run(FlexibleProfileSettings settings)
        {
            RunCount++;
            ReleaseRows();
            EnsurePresetFlags(settings);
            List<string> paths = !string.IsNullOrEmpty(targetPrefab)
                ? new List<string> { targetPrefab }
                : FindTargets(folder != null ? AssetDatabase.GetAssetPath(folder) : "Assets");
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
                        Vector2 logical = FlexiblePreviewRenderer.GetLogicalSize(screen.aspect, settings.ReferenceShortSide);
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

        private static string Summarize(GameObject asset, FlexibleProfileSettings settings)
        {
            var layout = asset.GetComponent<FlexibleLayout>();
            if (layout == null)
                return "";

            var parts = new List<string>();
            foreach (FlexibleProfile profile in settings.Profiles)
            {
                if (profile.id == settings.BaseProfileId)
                    continue;
                parts.Add(F("{0}: {1} changed", FlexibleEditPanel.Name(profile), FlexibleScreenSession.CountRecords(layout, profile.id)));
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
