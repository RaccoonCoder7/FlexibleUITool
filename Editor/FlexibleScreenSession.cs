using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // 블록 하나가 지금 보고 있는 화면에서 어떤 상태인지
    public enum FlexibleBlockStatus
    {
        BaseScreen,   // 기준(세로) 화면을 보고 있다
        SameAsBase,   // 기준 화면과 같은 배치
        Own,          // 이 화면만의 배치
        NotSavedYet,  // 저장 전에 새로 만든 블록 (저장하면 모든 화면에 생긴다)
    }

    // 프리팹 모드에서 세로·정사각·가로 화면을 버튼으로 바꿔 가며 같은 프리팹을 편집한다.
    // 프리팹 파일에는 항상 기준 화면 배치 + 화면별 기록만 저장된다: 저장 직전에 지금 화면을 기록하고 기준으로 되돌렸다가, 저장 후 다시 적용한다
    [InitializeOnLoad]
    public static class FlexibleScreenSession
    {
        private const string AutoGameViewKey = "FlexibleUITool.AutoGameView";
        private const string PreviousGameViewKey = "FlexibleUITool.PreviousGameViewIndex";
        private const string ViewKeyPrefix = "FlexibleUITool.View:";
        private const HideFlags SessionFlags = HideFlags.DontSaveInEditor | HideFlags.HideInInspector | HideFlags.NotEditable;

        private static readonly MethodInfo SavePrefabMethod =
            typeof(PrefabStage).GetMethod("SavePrefab", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null);

        private static bool busy;
        private static string reapplyProfile;
        private static string diskMapPath;
        private static GameObject diskMapAsset;
        private static Dictionary<string, RectTransform> diskMap;
        private static double nextShapeCheck;

        // 화면이 바뀌거나 저장됐을 때 (창 다시 그리기용)
        public static event Action Changed;

        // 프리팹을 저장한 횟수 (튜토리얼 확인용)
        public static int SaveCount { get; private set; }

        // 화면을 바꾸면 Game 뷰도 그 화면의 작업 해상도로 바꾼다 (프리팹을 닫으면 원래 해상도로 돌아간다)
        public static bool AutoGameView
        {
            get => EditorPrefs.GetBool(AutoGameViewKey, true);
            set => EditorPrefs.SetBool(AutoGameViewKey, value);
        }

        static FlexibleScreenSession()
        {
            PrefabStage.prefabSaving += OnSaving;
            PrefabStage.prefabSaved += OnSaved;
            PrefabStage.prefabStageOpened += OnOpened;
            PrefabStage.prefabStageClosing += OnClosing;
            EditorApplication.projectChanged += () => diskMap = null;
            // 도메인 리로드 뒤에는 열림 이벤트가 오지 않으므로 다시 확인한다
            EditorApplication.delayCall += () => Prepare(PrefabStageUtility.GetCurrentPrefabStage());
            SceneView.duringSceneGui += _ =>
            {
                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null)
                    EnsureEnvironmentScaler(stage);
            };
            EditorApplication.update += AutoFixShape;
        }

        // ───────── 조회 ─────────

        // 화면 전환을 쓸 수 있는 프리팹 모드인지 (UI 루트 + FlexibleLayout + 프로필 설정, 옛 작업 프리팹은 제외)
        public static bool IsReady(PrefabStage stage, out FlexibleLayout layout)
        {
            layout = null;
            if (stage == null || stage.prefabContentsRoot == null || !(stage.prefabContentsRoot.transform is RectTransform))
                return false;
            if (IsLegacyWorkspace(stage))
                return false;

            layout = stage.prefabContentsRoot.GetComponent<FlexibleLayout>();
            return layout != null && layout.Settings != null && layout.Settings.Profiles.Count > 0;
        }

        // 예전 버전의 작업 프리팹(Assets/FlexibleUITool/Workspace/...)을 열었는지
        public static bool IsLegacyWorkspace(PrefabStage stage)
        {
            if (stage == null || stage.prefabContentsRoot == null)
                return false;
            var marker = stage.prefabContentsRoot.GetComponent<FlexibleWorkspace>();
            return marker != null && !marker.inPlace;
        }

        public static FlexibleWorkspace GetSession(PrefabStage stage)
        {
            if (stage == null || stage.prefabContentsRoot == null)
                return null;
            var marker = stage.prefabContentsRoot.GetComponent<FlexibleWorkspace>();
            return marker != null && marker.inPlace ? marker : null;
        }

        public static string GetActiveProfileId(PrefabStage stage)
        {
            if (!IsReady(stage, out FlexibleLayout layout))
                return null;
            FlexibleWorkspace session = GetSession(stage);
            return session != null && !string.IsNullOrEmpty(session.profileId) ? session.profileId : layout.Settings.BaseProfileId;
        }

        public static FlexibleProfile GetActiveProfile(PrefabStage stage)
        {
            return IsReady(stage, out FlexibleLayout layout) ? layout.Settings.Find(GetActiveProfileId(stage)) : null;
        }

        public static bool IsBaseView(PrefabStage stage)
        {
            return IsReady(stage, out FlexibleLayout layout) && GetActiveProfileId(stage) == layout.Settings.BaseProfileId;
        }

        // 프리팹 모양(= Game 뷰 해상도)이 지금 편집 중인 화면의 비율 범위에 들어 있는지. 아니면 profile에 그 화면을 돌려준다
        public static bool IsShapeMatching(PrefabStage stage, out FlexibleProfile profile)
        {
            profile = null;
            if (!IsReady(stage, out FlexibleLayout layout))
                return true;

            FlexibleProfileSettings settings = layout.Settings;
            int index = settings.IndexOf(GetActiveProfileId(stage));
            if (index < 0)
                return true;

            Rect rect = ((RectTransform)stage.prefabContentsRoot.transform).rect;
            if (rect.width <= 0f || rect.height <= 0f)
                return true;

            float aspect = rect.width / rect.height;
            if (aspect >= settings.Profiles[index].minAspect && aspect < settings.MaxAspectOf(index))
                return true;
            profile = settings.Profiles[index];
            return false;
        }

        // 이 화면만의 배치를 가진 블록 수 (프리팹 모드 내용 기준, 저장 전 변경은 저장할 때 반영된다)
        public static int CountRecords(FlexibleLayout layout, string profileId)
        {
            int count = 0;
            foreach (FlexibleLayoutEntry entry in layout.Entries)
            {
                if (entry.target != null && entry.Find(profileId) != null)
                    count++;
            }
            return count;
        }

        public static FlexibleBlockStatus GetStatus(PrefabStage stage, RectTransform target)
        {
            if (IsBaseView(stage))
                return FlexibleBlockStatus.BaseScreen;

            RectTransform saved = FindSaved(stage, target);
            if (saved == null)
                return FlexibleBlockStatus.NotSavedYet;
            return FlexibleWorkspaceService.TryBuildState(target, saved, null, out _, out _) ? FlexibleBlockStatus.Own : FlexibleBlockStatus.SameAsBase;
        }

        // ───────── 화면 전환·저장 ─────────

        // 지금 화면을 저장하고 다른 화면으로 바꾼다 (같은 화면이면 저장만)
        public static bool ShowProfile(PrefabStage stage, string profileId)
        {
            return Commit(stage, profileId, null);
        }

        public static bool Save(PrefabStage stage)
        {
            return Commit(stage, GetActiveProfileId(stage), null);
        }

        // 한 화면의 기록을 모두 지운다 (그 화면은 기준 화면과 같아진다)
        public static bool ClearProfile(PrefabStage stage, string profileId)
        {
            return Commit(stage, GetActiveProfileId(stage), layout =>
            {
                foreach (FlexibleLayoutEntry entry in layout.Entries)
                    entry.states.RemoveAll(s => s.profileId == profileId);
                layout.Entries.RemoveAll(e => e.target == null || e.states.Count == 0);
            });
        }

        // 선택한 블록을 기준 화면 배치로 되돌린다 (Undo 가능, 저장하면 이 화면의 기록에서 빠진다)
        public static void ResetToBase(PrefabStage stage, RectTransform target)
        {
            RectTransform saved = FindSaved(stage, target);
            if (saved == null)
                return;

            LayoutGroup group = target.GetComponent<LayoutGroup>();
            var objects = new List<UnityEngine.Object> { target, target.gameObject };
            if (group != null)
                objects.Add(group);
            FlexibleWorkspace session = GetSession(stage);
            if (session != null)
                objects.Add(session);
            Undo.RecordObjects(objects.ToArray(), "Flexible Reset To Base");
            ApplySaved(saved, target);
            if (session != null)
                session.placements.RemoveAll(p => p.target == target);
            Changed?.Invoke();
        }

        // modify는 기준 배치로 되돌린 뒤 저장 직전에 FlexibleLayout 데이터를 고칠 기회
        private static bool Commit(PrefabStage stage, string viewAfter, Action<FlexibleLayout> modify)
        {
            if (!IsReady(stage, out FlexibleLayout layout))
                return false;

            FlexibleProfileSettings settings = layout.Settings;
            if (settings.Find(viewAfter) == null)
                viewAfter = settings.BaseProfileId;

            FlexibleWorkspace session = Prepare(stage);
            string active = GetActiveProfileId(stage);
            string baseId = settings.BaseProfileId;
            var root = (RectTransform)stage.prefabContentsRoot.transform;
            var kept = new List<FlexibleGridPlacement>(session.placements);
            bool ok;

            busy = true;
            try
            {
                if (active != baseId)
                {
                    Record(stage, layout, session, active);
                    RestoreBase(stage, session);
                }

                modify?.Invoke(layout);
                ok = InvokeSave(stage);
                if (ok)
                    SaveCount++;
                diskMap = null;

                // 이제 내용 = 저장된 기준 배치
                RebuildLinks(session, root);
                session.placements.Clear();
                if (viewAfter == baseId)
                {
                    session.placements.AddRange(active == baseId ? kept : session.basePlacements);
                }
                else
                {
                    if (active == baseId)
                        session.basePlacements = kept;
                    ApplyProfileStates(layout, session, viewAfter);
                }
                session.profileId = viewAfter;
                SessionState.SetString(ViewKeyPrefix + stage.assetPath, viewAfter);
                session.placements.RemoveAll(p => p.target == null);
                stage.ClearDirtiness();
                if (active != viewAfter)
                    ClearUndo(root, session);
            }
            finally
            {
                busy = false;
            }

            if (active != viewAfter && AutoGameView)
                SelectWorkResolution(settings.Find(viewAfter));
            Changed?.Invoke();
            SceneView.RepaintAll();
            return ok;
        }

        // Ctrl+S·자동 저장: 지금 화면을 기록하고 기준 배치로 되돌린 상태로 저장되게 한다
        private static void OnSaving(GameObject root)
        {
            if (busy)
                return;

            PrefabStage stage = PrefabStageUtility.GetPrefabStage(root);
            if (!IsReady(stage, out FlexibleLayout layout))
                return;

            FlexibleWorkspace session = Prepare(stage);
            string active = GetActiveProfileId(stage);
            reapplyProfile = null;
            if (active == layout.Settings.BaseProfileId)
                return;

            Record(stage, layout, session, active);
            RestoreBase(stage, session);
            reapplyProfile = active;
        }

        private static void OnSaved(GameObject root)
        {
            if (busy)
                return;

            diskMap = null;
            PrefabStage stage = PrefabStageUtility.GetPrefabStage(root);
            if (!IsReady(stage, out FlexibleLayout layout))
                return;

            SaveCount++;
            FlexibleWorkspace session = Prepare(stage);
            RebuildLinks(session, (RectTransform)root.transform);
            if (!string.IsNullOrEmpty(reapplyProfile))
            {
                session.placements.Clear();
                ApplyProfileStates(layout, session, reapplyProfile);
                reapplyProfile = null;
                // 다시 적용한 값은 이미 저장된 화면 기록이라 저장할 변경이 아니다
                stage.ClearDirtiness();
                EditorApplication.delayCall += () =>
                {
                    if (stage != null && stage.prefabContentsRoot != null)
                        stage.ClearDirtiness();
                };
            }
            Changed?.Invoke();
        }

        private static bool InvokeSave(PrefabStage stage)
        {
            if (SavePrefabMethod != null)
            {
                try
                {
                    return (bool)SavePrefabMethod.Invoke(stage, null);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[FlexibleUITool] " + e.GetBaseException().Message);
                }
            }

            // 내부 API가 바뀌었으면 직접 저장한다
            PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath, out bool success);
            stage.ClearDirtiness();
            return success;
        }

        // ───────── 기록·복원 ─────────

        // 저장된 기준 배치와 값이 다른 블록만 이 화면의 기록으로 남기고, 이 화면의 예전 기록은 모두 교체한다
        private static void Record(PrefabStage stage, FlexibleLayout layout, FlexibleWorkspace session, string profileId)
        {
            foreach (FlexibleLayoutEntry entry in layout.Entries)
                entry.states.RemoveAll(s => s.profileId == profileId);

            foreach (FlexibleWorkspaceLink link in session.links)
            {
                if (link.target == null)
                    continue;
                RectTransform saved = FindSavedByPath(stage, link.sourcePath);
                if (saved == null)
                    continue;
                if (!FlexibleWorkspaceService.TryBuildState(link.target, saved, profileId, out FlexibleProfileState state, out LayoutGroup savedGroup))
                    continue;

                FlexibleWorkspaceService.MarkGridPlacement(state, session.FindPlacement(link.target));
                FlexibleLayoutEntry target = layout.Entries.Find(e => e.target == link.target);
                if (target == null)
                {
                    target = new FlexibleLayoutEntry { target = link.target };
                    layout.Entries.Add(target);
                }
                if (savedGroup != null)
                    target.layoutGroup = link.target.GetComponent<LayoutGroup>();
                target.states.Add(state);
            }

            layout.Entries.RemoveAll(e => e.target == null || e.states.Count == 0);
            layout.Roles.RemoveAll(r => r.target == null || string.IsNullOrEmpty(r.role));
        }

        private static void RestoreBase(PrefabStage stage, FlexibleWorkspace session)
        {
            foreach (FlexibleWorkspaceLink link in session.links)
            {
                if (link.target == null)
                    continue;
                RectTransform saved = FindSavedByPath(stage, link.sourcePath);
                if (saved != null)
                    ApplySaved(saved, link.target);
            }
        }

        private static void ApplySaved(RectTransform saved, RectTransform target)
        {
            FlexibleRectState.Capture(saved).ApplyTo(target);
            if (target.gameObject.activeSelf != saved.gameObject.activeSelf)
                target.gameObject.SetActive(saved.gameObject.activeSelf);

            var savedGroup = saved.GetComponent<LayoutGroup>();
            var group = target.GetComponent<LayoutGroup>();
            if (savedGroup != null && group != null && savedGroup.GetType() == group.GetType())
                FlexibleLayoutGroupState.Capture(savedGroup).ApplyTo(group);
        }

        // 내용이 기준 배치인 상태에서 부른다. 그리드로 놓았던 기록은 배치 기록으로 되살린다
        private static void ApplyProfileStates(FlexibleLayout layout, FlexibleWorkspace session, string profileId)
        {
            foreach (FlexibleLayoutEntry entry in layout.Entries)
            {
                FlexibleProfileState state = entry.target != null ? entry.Find(profileId) : null;
                if (state == null)
                    continue;

                // 적용 전 스케일이 기준 값. 그리드 결과에는 이 값을 쓴다
                Vector3 baseScale = entry.target.localScale;
                state.ApplyTo(entry);
                if (!state.gridOrigin && !state.gridPlaced)
                    continue;

                FlexibleRectState result = state.gridOrigin ? state.gridResult : state.rect;
                result.localScale = baseScale;
                session.placements.Add(new FlexibleGridPlacement
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

        private static void RebuildLinks(FlexibleWorkspace session, RectTransform root)
        {
            session.links.Clear();
            foreach (RectTransform rt in FlexiblePathUtility.CollectTargets(root))
                session.links.Add(new FlexibleWorkspaceLink { sourcePath = FlexiblePathUtility.GetPath(root, rt), target = rt });
        }

        // 화면을 바꾼 뒤에는 다른 화면의 편집을 되돌리지 않도록 Undo 기록을 지운다
        private static void ClearUndo(RectTransform root, FlexibleWorkspace session)
        {
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component != null)
                    Undo.ClearUndo(component);
            }
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                Undo.ClearUndo(t.gameObject);
            Undo.ClearUndo(session);
        }

        // 저장된 프리팹(디스크) 쪽 같은 블록
        public static RectTransform FindSaved(PrefabStage stage, RectTransform target)
        {
            FlexibleWorkspace session = GetSession(stage);
            FlexibleWorkspaceLink link = session != null ? session.links.Find(l => l.target == target) : null;
            return link != null ? FindSavedByPath(stage, link.sourcePath) : null;
        }

        private static RectTransform FindSavedByPath(PrefabStage stage, string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(stage.assetPath);
            if (asset == null || !(asset.transform is RectTransform assetRoot))
                return null;

            if (diskMap == null || diskMapPath != stage.assetPath || diskMapAsset != asset)
            {
                diskMap = FlexiblePathUtility.BuildMap(assetRoot);
                diskMapPath = stage.assetPath;
                diskMapAsset = asset;
            }
            return diskMap.TryGetValue(path, out RectTransform saved) && saved != null ? saved : null;
        }

        // ───────── 세션 표식 ─────────

        // 세션 표식을 붙인다 (없으면 기준 화면으로 시작). 표식이 사라졌는데 기준 화면이 아니었다면 저장된 기준 배치로 되돌린다
        private static FlexibleWorkspace Prepare(PrefabStage stage)
        {
            if (!IsReady(stage, out FlexibleLayout layout))
                return null;

            FlexibleWorkspace session = GetSession(stage);
            if (session != null)
                return session;

            GameObject root = stage.prefabContentsRoot;
            bool wasDirty = stage.scene.isDirty;
            session = root.AddComponent<FlexibleWorkspace>();
            session.hideFlags = SessionFlags;
            session.inPlace = true;
            session.sourceGuid = AssetDatabase.AssetPathToGUID(stage.assetPath);
            session.profileId = layout.Settings.BaseProfileId;
            RebuildLinks(session, (RectTransform)root.transform);

            string remembered = SessionState.GetString(ViewKeyPrefix + stage.assetPath, "");
            if (!string.IsNullOrEmpty(remembered) && remembered != layout.Settings.BaseProfileId)
            {
                RestoreBase(stage, session);
                Debug.Log("[FlexibleUITool] " + T("The screen view was reset to the base screen after a script reload."));
            }
            SessionState.SetString(ViewKeyPrefix + stage.assetPath, session.profileId);
            if (!wasDirty)
                stage.ClearDirtiness();
            return session;
        }

        private static void OnOpened(PrefabStage stage)
        {
            EnsureEnvironmentScaler(stage);
            SessionState.EraseString(ViewKeyPrefix + stage.assetPath);
            if (Prepare(stage) == null)
                return;

            if (AutoGameView && IsReady(stage, out FlexibleLayout layout))
                SelectWorkResolution(layout.Settings.Find(layout.Settings.BaseProfileId));
            Changed?.Invoke();
        }

        private static void OnClosing(PrefabStage stage)
        {
            SessionState.EraseString(ViewKeyPrefix + stage.assetPath);
            int previous = SessionState.GetInt(PreviousGameViewKey, -1);
            SessionState.EraseInt(PreviousGameViewKey);
            if (previous >= 0)
                FlexibleGameViewUtility.TrySelectIndex(previous);
            EditorApplication.delayCall += () => Changed?.Invoke();
        }

        // ───────── Game 뷰·환경 캔버스 ─────────

        // 화면과 프리팹 모양이 어긋나 있으면 (예: 세로인데 Game 뷰가 가로 해상도) Game 뷰를 그 화면 크기로 맞춘다.
        // 다른 해상도로 작업하고 싶으면 편집 탭 「그 밖」에서 자동 변경을 끈다
        private static void AutoFixShape()
        {
            if (EditorApplication.timeSinceStartup < nextShapeCheck)
                return;
            nextShapeCheck = EditorApplication.timeSinceStartup + 0.5;
            if (busy || !AutoGameView || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                return;

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (!IsShapeMatching(stage, out FlexibleProfile profile))
                SelectWorkResolution(profile);
        }

        // 처음 바꾸기 전의 Game 뷰 해상도를 기억해 두었다가 프리팹을 닫을 때 되돌린다
        public static bool SelectWorkResolution(FlexibleProfile profile)
        {
            if (profile == null)
                return false;

            if (SessionState.GetInt(PreviousGameViewKey, -1) < 0 && FlexibleGameViewUtility.TryGetSelectedIndex(out int previous))
                SessionState.SetInt(PreviousGameViewKey, previous);

            int width = Mathf.RoundToInt(profile.workResolution.x);
            int height = Mathf.RoundToInt(profile.workResolution.y);
            return FlexibleGameViewUtility.TrySelectResolution(width, height, $"Flexible {profile.displayName} {width}x{height}");
        }

        // 환경 캔버스에 짧은 변 기준 스케일러를 붙인다 (실행 화면의 FlexibleCanvasScaler와 같은 크기로 보이게)
        public static void EnsureEnvironmentScaler(PrefabStage stage)
        {
            GameObject root = stage.prefabContentsRoot;
            if (root == null || root.GetComponent<Canvas>() != null)
                return;
            if (root.GetComponent<FlexibleLayout>() == null && root.GetComponent<FlexibleWorkspace>() == null)
                return;

            Transform environment = root.transform.parent;
            if (environment == null || environment.GetComponent<Canvas>() == null || environment.GetComponent<CanvasScaler>() != null)
                return;

            FlexibleProfileSettings settings = FlexibleSettingsUtility.GetOrCreateDefault();
            FlexibleProfile baseProfile = settings.Find(settings.BaseProfileId);
            Vector2 reference = baseProfile != null ? baseProfile.workResolution : new Vector2(1080, 1920);

            var scaler = environment.gameObject.AddComponent<FlexibleCanvasScaler>();
            scaler.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = reference;
        }
    }
}
