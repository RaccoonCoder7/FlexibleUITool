using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // FlexibleLayout이 붙은 모든 프리팹을 기기 프리셋별로 렌더링해 한 화면에서 검수하는 창
    public class FlexibleReviewWindow : EditorWindow
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

        private DefaultAsset folder;
        private float thumbnailHeight = 200f;
        private float renderedHeight; // 마지막 검수 때 썸네일을 그린 높이 (화면 단위)
        private bool[] presetEnabled;
        private bool onlyIssues;
        private bool includeEdges = true;
        private string targetPrefab; // 지정되면 이 프리팹만 검수 (CLI review -Prefab)
        private Vector2 scroll;
        private readonly List<Row> rows = new List<Row>();

        [MenuItem("Tools/FlexibleUITool/Review Window")]
        public static void Open()
        {
            GetWindow<FlexibleReviewWindow>("Flexible Review");
        }

        private void OnDisable()
        {
            ReleaseRows();
        }

        // 창을 열고 바로 검수한다 (CLI review). 프리팹을 주면 그것만, 아니면 폴더(비우면 Assets 전체)
        public static FlexibleReviewWindow OpenAndRun(string folderPath, string prefabPath)
        {
            var window = GetWindow<FlexibleReviewWindow>("Flexible Review");
            window.folder = string.IsNullOrEmpty(folderPath) ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
            window.targetPrefab = string.IsNullOrEmpty(prefabPath) ? null : prefabPath;
            window.onlyIssues = false;
            window.Run(FlexibleSettingsUtility.GetOrCreateDefault());
            window.Show();
            window.Focus();
            window.Repaint();
            return window;
        }

        // 검수 결과 요약 (프리팹, 화면 이름, 프로필 id, 문제 문장). 문장은 부르는 시점의 언어
        public IEnumerable<(string prefab, string screen, string profile, List<string> issues)> Results =>
            rows.SelectMany(r => r.cells.Select(c => (r.path, c.ScreenName, c.profile.id, c.Messages)));

        private void EnsurePresetFlags(FlexibleProfileSettings settings)
        {
            if (presetEnabled == null || presetEnabled.Length != settings.PreviewPresets.Count)
                presetEnabled = Enumerable.Repeat(true, settings.PreviewPresets.Count).ToArray();
        }

        private void OnGUI()
        {
            FlexibleProfileSettings settings = FlexibleSettingsUtility.GetOrCreateDefault();
            EnsurePresetFlags(settings);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (!string.IsNullOrEmpty(targetPrefab))
                {
                    GUILayout.Label(F("Target: {0}", Path.GetFileNameWithoutExtension(targetPrefab)), GUILayout.ExpandWidth(false));
                    if (GUILayout.Button(T("All"), EditorStyles.toolbarButton, GUILayout.Width(48f)))
                        targetPrefab = null;
                }
                else
                {
                    GUILayout.Label(T("Folder"), GUILayout.ExpandWidth(false));
                    folder = (DefaultAsset)EditorGUILayout.ObjectField(folder, typeof(DefaultAsset), false, GUILayout.Width(180f));
                }
                GUILayout.Label(T("Thumbnail"), GUILayout.ExpandWidth(false));
                thumbnailHeight = GUILayout.HorizontalSlider(thumbnailHeight, 100f, 400f, GUILayout.Width(100f));
                onlyIssues = GUILayout.Toggle(onlyIssues, T("Only issues"), EditorStyles.toolbarButton);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(T("Run review"), EditorStyles.toolbarButton, GUILayout.Width(90f)))
                {
                    Run(settings);
                    GUIUtility.ExitGUI();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < settings.PreviewPresets.Count; i++)
                    presetEnabled[i] = GUILayout.Toggle(presetEnabled[i], T(settings.PreviewPresets[i].name), EditorStyles.miniButton);
                includeEdges = GUILayout.Toggle(includeEdges, new GUIContent(T("Profile edge ratios"), T("Also checks the narrowest and widest ratio where each profile can appear. Fixed-size blocks overflow there first.")), EditorStyles.miniButton);
            }

            if (rows.Count == 0)
            {
                EditorGUILayout.HelpBox(F("Press [{0}] to render every prefab with FlexibleLayout on each device preset (and profile edge ratio). Leave the folder empty to search all of Assets.", T("Run review")), MessageType.Info);
                return;
            }

            if (thumbnailHeight > renderedHeight * 1.1f)
                EditorGUILayout.HelpBox(F("Thumbnails were rendered smaller. Press [{0}] again for sharp thumbnails at this size.", T("Run review")), MessageType.None);

            int total = rows.Sum(r => r.cells.Sum(c => c.issues.Count));
            EditorGUILayout.LabelField(F("{0} prefabs · {1} issues", rows.Count, total), EditorStyles.boldLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (Row row in rows)
            {
                if (onlyIssues && row.cells.All(c => c.issues.Count == 0))
                    continue;
                DrawRow(row);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawRow(Row row)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(Path.GetFileNameWithoutExtension(row.path), EditorStyles.boldLabel, GUILayout.Width(220f));
                    EditorGUILayout.LabelField(Summarize(AssetDatabase.LoadAssetAtPath<GameObject>(row.path), FlexibleSettingsUtility.GetOrCreateDefault()), EditorStyles.miniLabel);
                    if (GUILayout.Button(T("Select"), GUILayout.Width(60f)))
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(row.path);
                        Selection.activeObject = asset;
                        EditorGUIUtility.PingObject(asset);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    foreach (Cell cell in row.cells)
                    {
                        if (!onlyIssues || cell.issues.Count > 0)
                            DrawCell(cell);
                    }
                    GUILayout.FlexibleSpace();
                }
            }
        }

        private void DrawCell(Cell cell)
        {
            float width = thumbnailHeight * cell.texture.width / cell.texture.height;
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Max(width, 120f))))
            {
                GUILayout.Label($"{cell.ScreenName}\n→ {Label(cell.profile)}", EditorStyles.miniLabel);
                Rect rect = GUILayoutUtility.GetRect(width, thumbnailHeight, GUILayout.Width(width), GUILayout.Height(thumbnailHeight));
                GUI.DrawTexture(rect, cell.texture, ScaleMode.StretchToFill, false);
                if (cell.issues.Count > 0)
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 3f), new Color(1f, 0.4f, 0.3f));

                var style = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = cell.issues.Count > 0 ? new Color(1f, 0.55f, 0.45f) : Color.gray } };
                List<string> messages = cell.Messages;
                string text = messages.Count == 0 ? T("No issues") : string.Join("\n", messages.Take(4)) + (messages.Count > 4 ? "\n" + F("{0} more", messages.Count - 4) : "");
                GUILayout.Label(new GUIContent(text, string.Join("\n", messages)), style, GUILayout.Width(Mathf.Max(width, 120f)));
            }
        }

        private void Run(FlexibleProfileSettings settings)
        {
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

            using (var renderer = new FlexiblePreviewRenderer())
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Flexible Review", paths[i], (float)i / paths.Count))
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
                        renderer.Render(asset, profile.id, logical, pixel, screen.safeArea);

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

                List<FlexibleProfileState> states = layout.Entries.Select(e => e.Find(profile.id)).Where(s => s != null).ToList();
                int grid = states.Count(s => s.gridPlaced);
                parts.Add(F("{0} {1} ({2} grid / {3} manual)", T(profile.displayName), states.Count, grid, states.Count - grid));
            }
            parts.Add(F("{0} roles", layout.Roles.Count));
            return string.Join("   ", parts);
        }

        private void ReleaseRows()
        {
            foreach (Cell cell in rows.SelectMany(r => r.cells))
            {
                if (cell.texture == null)
                    continue;
                cell.texture.Release();
                DestroyImmediate(cell.texture);
            }
            rows.Clear();
        }
    }
}
