using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // 반영 결과
    public class FlexibleCommitReport
    {
        public bool success;
        public bool dryRun;
        public string sourcePath;
        public string profileId;
        public int recorded;
        public readonly List<string> warnings = new List<string>();
        public readonly List<FlexibleChange> changes = new List<FlexibleChange>();
    }

    // 반영으로 프로필 기록이 달라지는 블록 하나 (kind = added / updated / removed)
    public class FlexibleChange
    {
        public string path;
        public string role;
        public string kind;
    }

    // 원본 프리팹 → 작업 프리팹 생성, 작업 프리팹 → 원본 프로필 데이터 반영, 작업 프리팹 폐기
    public static class FlexibleWorkspaceService
    {
        public const string WorkspaceFolder = FlexibleSettingsUtility.DataFolder + "/Workspace";
        private const float Epsilon = 0.01f;

        public static string GetWorkspacePath(string sourceGuid, string profileId)
        {
            string name = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(sourceGuid));
            return $"{WorkspaceFolder}/{name}__{profileId}__{sourceGuid.Substring(0, 8)}.prefab";
        }

        public static bool WorkspaceExists(string sourcePath, string profileId)
        {
            string path = GetWorkspacePath(AssetDatabase.AssetPathToGUID(sourcePath), profileId);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
        }

        // 이미 있으면 그대로 열고, 없으면 최신 원본을 복사해 프로필 데이터를 적용한 뒤 연다
        public static void OpenOrCreate(string sourcePath, string profileId)
        {
            string guid = AssetDatabase.AssetPathToGUID(sourcePath);
            string workspacePath = GetWorkspacePath(guid, profileId);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(workspacePath) == null)
                Create(sourcePath, guid, profileId, workspacePath);

            PrefabStageUtility.OpenPrefab(workspacePath);
        }

        public static bool IsSourceModified(string workspacePath)
        {
            FlexibleWorkspace workspace = LoadWorkspaceComponent(workspacePath);
            if (workspace == null)
                return false;

            string sourcePath = AssetDatabase.GUIDToAssetPath(workspace.sourceGuid);
            return !string.IsNullOrEmpty(sourcePath) && ComputeHash(sourcePath) != workspace.sourceHash;
        }

        public static FlexibleWorkspace LoadWorkspaceComponent(string workspacePath)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(workspacePath);
            return asset != null ? asset.GetComponent<FlexibleWorkspace>() : null;
        }

        // dryRun이면 원본에 쓰지 않고 바뀔 내용(changes)만 계산한다
        public static FlexibleCommitReport Commit(string workspacePath, bool dryRun = false)
        {
            var report = new FlexibleCommitReport { dryRun = dryRun };
            GameObject workspaceRoot = PrefabUtility.LoadPrefabContents(workspacePath);
            GameObject sourceRoot = null;
            try
            {
                var workspace = workspaceRoot.GetComponent<FlexibleWorkspace>();
                if (workspace == null)
                {
                    report.warnings.Add(T("No workspace marker (FlexibleWorkspace) found."));
                    return report;
                }

                report.profileId = workspace.profileId;
                report.sourcePath = AssetDatabase.GUIDToAssetPath(workspace.sourceGuid);
                if (string.IsNullOrEmpty(report.sourcePath) || AssetDatabase.LoadAssetAtPath<GameObject>(report.sourcePath) == null)
                {
                    report.warnings.Add(T("Source prefab not found."));
                    return report;
                }

                sourceRoot = PrefabUtility.LoadPrefabContents(report.sourcePath);
                var layout = sourceRoot.GetComponent<FlexibleLayout>();
                if (layout == null)
                {
                    layout = sourceRoot.AddComponent<FlexibleLayout>();
                    layout.Settings = FlexibleSettingsUtility.GetOrCreateDefault();
                }

                RecordProfile(workspace, (RectTransform)workspaceRoot.transform, (RectTransform)sourceRoot.transform, layout, report);
                if (dryRun)
                {
                    report.success = true;
                    return report;
                }

                PrefabUtility.SaveAsPrefabAsset(sourceRoot, report.sourcePath, out report.success);
                if (!report.success)
                    report.warnings.Add(T("Failed to save the source prefab."));
                return report;
            }
            finally
            {
                if (sourceRoot != null)
                    PrefabUtility.UnloadPrefabContents(sourceRoot);
                PrefabUtility.UnloadPrefabContents(workspaceRoot);
            }
        }

        public static void Delete(string workspacePath)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == workspacePath)
            {
                // 삭제할 파일이므로 저장 확인 없이 닫는다
                stage.ClearDirtiness();
                StageUtility.GoToMainStage();
            }

            AssetDatabase.DeleteAsset(workspacePath);
        }

        // 창에 열지 않는 작업 프리팹을 원하는 경로에 만든다 (CLI·자동화용)
        public static void CreateAt(string sourcePath, string profileId, string workspacePath)
        {
            Create(sourcePath, AssetDatabase.AssetPathToGUID(sourcePath), profileId, workspacePath);
        }

        private static void Create(string sourcePath, string guid, string profileId, string workspacePath)
        {
            FlexibleSettingsUtility.EnsureFolder(WorkspaceFolder);
            // 작업 프리팹은 임시 파일이라 git에 올리지 않는다
            string ignore = Path.Combine(WorkspaceFolder, ".gitignore");
            if (!File.Exists(ignore))
                File.WriteAllText(ignore, "*\n!.gitignore\n");
            if (!AssetDatabase.CopyAsset(sourcePath, workspacePath))
                throw new InvalidOperationException(F("Could not copy the workspace: {0} → {1}", sourcePath, workspacePath));

            GameObject root = PrefabUtility.LoadPrefabContents(workspacePath);
            try
            {
                var rootRect = (RectTransform)root.transform;
                var workspace = root.AddComponent<FlexibleWorkspace>();
                workspace.sourceGuid = guid;
                workspace.profileId = profileId;
                workspace.sourceHash = ComputeHash(sourcePath);
                foreach (RectTransform rt in FlexiblePathUtility.CollectTargets(rootRect))
                    workspace.links.Add(new FlexibleWorkspaceLink { sourcePath = FlexiblePathUtility.GetPath(rootRect, rt), target = rt });

                // 복사본의 FlexibleLayout 참조는 복사본 내부를 가리키므로 그대로 적용하고, 편집값을 덮어쓰지 않게 끈다
                var layout = root.GetComponent<FlexibleLayout>();
                if (layout != null)
                {
                    foreach (FlexibleLayoutEntry entry in layout.Entries)
                    {
                        FlexibleProfileState state = entry.target != null ? entry.Find(profileId) : null;
                        if (state == null)
                            continue;

                        // 적용 전 스케일이 원본 값. 그리드 결과에는 이 값을 쓴다 (예전 기록에 섞인 보정 스케일 정정)
                        Vector3 baseScale = entry.target.localScale;
                        state.ApplyTo(entry);
                        if (state.gridOrigin || state.gridPlaced)
                        {
                            FlexibleRectState result = state.gridOrigin ? state.gridResult : state.rect;
                            result.localScale = baseScale;
                            workspace.placements.Add(new FlexibleGridPlacement
                            {
                                target = entry.target,
                                cell = state.gridCell,
                                modeX = state.gridModeX,
                                modeY = state.gridModeY,
                                insideSafeArea = state.gridInsideSafeArea,
                                result = result,
                            });
                        }
                    }
                    layout.enabled = false;
                }

                PrefabUtility.SaveAsPrefabAsset(root, workspacePath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // 원본과 값이 다른 대상만 이 프로필 상태로 기록하고, 이전 기록은 모두 교체한다
        private static void RecordProfile(FlexibleWorkspace workspace, RectTransform workspaceRoot, RectTransform sourceRoot,
            FlexibleLayout layout, FlexibleCommitReport report)
        {
            string profileId = workspace.profileId;
            var previous = new Dictionary<RectTransform, FlexibleProfileState>();
            foreach (FlexibleLayoutEntry entry in layout.Entries)
            {
                FlexibleProfileState old = entry.target != null ? entry.Find(profileId) : null;
                if (old != null)
                    previous[entry.target] = old;
                entry.states.RemoveAll(s => s.profileId == profileId);
            }

            Dictionary<string, RectTransform> sourceMap = FlexiblePathUtility.BuildMap(sourceRoot);
            var linked = new HashSet<RectTransform>();
            var workspaceLayout = workspaceRoot.GetComponent<FlexibleLayout>();

            foreach (FlexibleWorkspaceLink link in workspace.links)
            {
                if (link.target == null)
                {
                    report.warnings.Add(F("Object deleted in the workspace (ignored): {0}", link.sourcePath));
                    continue;
                }

                linked.Add(link.target);
                if (!sourceMap.TryGetValue(link.sourcePath, out RectTransform source))
                {
                    report.warnings.Add(F("Object not found in the source (ignored): {0}", link.sourcePath));
                    continue;
                }

                // 역할은 프로필과 무관한 블록 속성이라 작업 프리팹 쪽 값으로 맞춘다
                if (workspaceLayout != null)
                    layout.SetRole(source, workspaceLayout.GetRole(link.target));

                if (TryBuildState(link.target, source, profileId, out FlexibleProfileState state, out LayoutGroup sourceGroup))
                {
                    MarkGridPlacement(state, workspace.FindPlacement(link.target));

                    FlexibleLayoutEntry entry = layout.Entries.Find(e => e.target == source);
                    if (entry == null)
                    {
                        entry = new FlexibleLayoutEntry { target = source };
                        layout.Entries.Add(entry);
                    }

                    if (sourceGroup != null)
                        entry.layoutGroup = sourceGroup;
                    entry.states.Add(state);
                    report.recorded++;

                    if (!previous.TryGetValue(source, out FlexibleProfileState old))
                        AddChange(report, layout, link.sourcePath, source, "added");
                    else if (!SameState(old, state))
                        AddChange(report, layout, link.sourcePath, source, "updated");
                    previous.Remove(source);
                }
            }

            foreach (RectTransform removed in previous.Keys)
            {
                if (removed != null)
                    AddChange(report, layout, FlexiblePathUtility.GetPath(sourceRoot, removed), removed, "removed");
            }

            layout.Entries.RemoveAll(e => e.target == null || e.states.Count == 0);
            layout.Roles.RemoveAll(r => r.target == null || string.IsNullOrEmpty(r.role));

            foreach (RectTransform rt in FlexiblePathUtility.CollectTargets(workspaceRoot))
            {
                if (!linked.Contains(rt))
                    report.warnings.Add(F("Object added in the workspace (not committed; add it to the source): {0}", FlexiblePathUtility.GetPath(workspaceRoot, rt)));
            }
        }

        private static bool TryBuildState(RectTransform edited, RectTransform source, string profileId,
            out FlexibleProfileState state, out LayoutGroup sourceGroup)
        {
            state = null;
            var editedGroup = edited.GetComponent<LayoutGroup>();
            sourceGroup = source.GetComponent<LayoutGroup>();
            if (editedGroup == null || sourceGroup == null || editedGroup.GetType() != sourceGroup.GetType())
                sourceGroup = null;

            FlexibleRectState editedRect = FlexibleRectState.Capture(edited);
            FlexibleRectState sourceRect = FlexibleRectState.Capture(source);

            // 레이아웃 그룹·피터가 정하는 값은 화면 크기에 따라 재계산되므로 비교에서 뺀다
            if (IsDrivenByParentLayout(edited))
                sourceRect = editedRect;
            else if (edited.GetComponent<ContentSizeFitter>() != null || edited.GetComponent<AspectRatioFitter>() != null)
                sourceRect.sizeDelta = editedRect.sizeDelta;

            bool rectChanged = !editedRect.Approximately(sourceRect, Epsilon);
            bool activeChanged = edited.gameObject.activeSelf != source.gameObject.activeSelf;
            FlexibleLayoutGroupState editedGroupState = default;
            bool groupChanged = false;
            if (sourceGroup != null)
            {
                editedGroupState = FlexibleLayoutGroupState.Capture(editedGroup);
                groupChanged = !editedGroupState.Approximately(FlexibleLayoutGroupState.Capture(sourceGroup), Epsilon);
            }

            if (!rectChanged && !activeChanged && !groupChanged)
                return false;

            state = new FlexibleProfileState
            {
                profileId = profileId,
                rect = rectChanged ? editedRect : FlexibleRectState.Capture(source),
                controlActive = activeChanged,
                active = edited.gameObject.activeSelf,
                controlLayoutGroup = groupChanged,
                layoutGroup = editedGroupState,
            };
            return true;
        }

        // 손대지 않았으면 그리드 블록, 손댔어도 그리드 출신이면 원래 배치 결과를 남겨 둔다 (템플릿의 칸+보정 계산용)
        private static void MarkGridPlacement(FlexibleProfileState state, FlexibleGridPlacement placement)
        {
            state.gridOrigin = placement != null;
            state.gridPlaced = placement != null && state.rect.Approximately(placement.result, Epsilon);
            if (!state.gridOrigin)
                return;

            state.gridResult = placement.result;
            state.gridCell = placement.cell;
            state.gridModeX = placement.modeX;
            state.gridModeY = placement.modeY;
            state.gridInsideSafeArea = placement.insideSafeArea;
        }

        private static void AddChange(FlexibleCommitReport report, FlexibleLayout layout, string path, RectTransform target, string kind)
        {
            report.changes.Add(new FlexibleChange { path = path, role = layout.GetRole(target), kind = kind });
        }

        private static bool SameState(FlexibleProfileState a, FlexibleProfileState b)
        {
            return a.rect.Approximately(b.rect, Epsilon)
                && a.controlActive == b.controlActive && (!a.controlActive || a.active == b.active)
                && a.controlLayoutGroup == b.controlLayoutGroup && (!a.controlLayoutGroup || a.layoutGroup.Approximately(b.layoutGroup, Epsilon));
        }

        public static bool IsUntouchedPlacement(FlexibleGridPlacement placement)
        {
            return placement.target != null && FlexibleRectState.Capture(placement.target).Approximately(placement.result, Epsilon);
        }

        private static bool IsDrivenByParentLayout(RectTransform rt)
        {
            if (rt.parent == null)
                return false;

            var parentGroup = rt.parent.GetComponent<LayoutGroup>();
            if (parentGroup == null || !parentGroup.enabled)
                return false;

            var element = rt.GetComponent<LayoutElement>();
            return element == null || !element.ignoreLayout;
        }

        private static string ComputeHash(string assetPath)
        {
            using var md5 = MD5.Create();
            byte[] bytes = File.ReadAllBytes(Path.GetFullPath(assetPath));
            return BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", string.Empty);
        }
    }
}
