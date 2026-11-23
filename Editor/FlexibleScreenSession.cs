using System;
using System.Collections.Generic;
using System.Linq;
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
        private const string OpenOnKeyPrefix = "FlexibleUITool.OpenOn:";
        private const HideFlags SessionFlags = HideFlags.DontSaveInEditor | HideFlags.HideInInspector | HideFlags.NotEditable;

        private static readonly MethodInfo SavePrefabMethod =
            typeof(PrefabStage).GetMethod("SavePrefab", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null);

        private static bool busy;
        private static string reapplyProfile;
        private static bool reshowView;
        private static string frameAfterSwitch;
        private static string diskMapPath;
        private static GameObject diskMapAsset;
        private static Dictionary<string, RectTransform> diskMap;
        private static GameObject fullDiskMapAsset;
        private static Dictionary<string, RectTransform> fullDiskMap;
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
            // 스크립트를 다시 불러온 뒤 글자 컴포넌트 등이 다시 계산되며 붙는 「변경 있음」을 지운다 (저장된 것과 같을 때만)
            // 다시 불러오기 전에 이미 바뀐 내용이 있었으면(사용자 편집) 건드리지 않는다
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                PrefabStage current = PrefabStageUtility.GetCurrentPrefabStage();
                SessionState.SetBool(CleanBeforeReloadKey, current != null && !current.scene.isDirty);
            };
            AssemblyReloadEvents.afterAssemblyReload += () =>
            {
                PrefabStage current = PrefabStageUtility.GetCurrentPrefabStage();
                if (current != null && SessionState.GetBool(CleanBeforeReloadKey, false))
                    CleanIfUnchangedLater(current);
                SessionState.EraseBool(CleanBeforeReloadKey);
            };
            PrefabStage.prefabStageClosing += OnClosing;
            EditorApplication.projectChanged += () => { diskMap = null; fullDiskMap = null; };
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

            if (!layout.TryGetSelectionSize(out Vector2 size))
                return true;

            // 그 화면 종류의 범위 안이라도 작업 해상도와 비율이 다르면 맞지 않는 것으로 본다.
            // 세로 원본을 3:4 크기로 편집하면 위치·크기가 그 크기 기준으로 저장되기 때문이다
            float aspect = size.x / size.y;
            Vector2 work = settings.Profiles[index].workResolution;
            float workAspect = work.y > 0f ? work.x / work.y : aspect;
            if (Mathf.Abs(aspect - workAspect) <= workAspect * 0.01f)
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

        // 한 화면의 기록 전체를 글로 (기록 전후가 같은지 비교용)
        private static string RecordsOf(FlexibleLayout layout, string profileId)
        {
            var text = new System.Text.StringBuilder();
            foreach (FlexibleLayoutEntry entry in layout.Entries)
            {
                FlexibleProfileState state = entry.target != null ? entry.Find(profileId) : null;
                if (state != null)
                    text.Append(entry.target.GetInstanceID()).Append(':').Append(JsonUtility.ToJson(state)).Append('|');
            }
            return text.ToString();
        }

        private static bool DiffersFromSaved(PrefabStage stage, FlexibleWorkspace session)
        {
            // 레이아웃 설정(전용 그리드·화면 고르는 기준·역할)
            var live = stage.prefabContentsRoot.GetComponent<FlexibleLayout>();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(stage.assetPath);
            var savedLayout = asset != null ? asset.GetComponent<FlexibleLayout>() : null;
            if (live == null || savedLayout == null || live.UseOwnGrid != savedLayout.UseOwnGrid || live.ProfileSource != savedLayout.ProfileSource
                || live.Roles.Count != savedLayout.Roles.Count || live.Entries.Count != savedLayout.Entries.Count)
                return true;
            if (live.Settings != null && live.Settings.Profiles.Any(p => !live.GetGrid(p.id).Equals(savedLayout.GetGrid(p.id))))
                return true;
            var root = stage.prefabContentsRoot.transform;
            var savedRoles = new HashSet<string>(savedLayout.Roles.Where(r => r.target != null).Select(r => FlexiblePathUtility.GetPath(asset.transform, r.target) + "=" + r.role));
            if (live.Roles.Any(r => r.target != null && !savedRoles.Contains(FlexiblePathUtility.GetPath(root, r.target) + "=" + r.role)))
                return true;

            foreach (FlexibleWorkspaceLink link in session.links)
            {
                if (link.target == null)
                    continue;
                RectTransform saved = FindSavedByPath(stage, link.sourcePath);
                if (saved == null || FlexibleWorkspaceService.TryBuildState(link.target, saved, null, out _, out _))
                    return true;
            }
            return false;
        }

        public static bool Save(PrefabStage stage)
        {
            return Commit(stage, GetActiveProfileId(stage), null);
        }

        // 툴이 다른 프리팹을 열기 전에 부른다. 열린 프리팹에 저장 안 한 변경이 있으면, Unity의 Prefab Mode 자동 저장이
        // 묻지 않고 저장해 버리므로 먼저 묻는다 (저장 / 취소 / 저장 안 함). 열어도 되면 true
        public static bool PrepareToOpen(string nextPath)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || stage.assetPath == nextPath || !stage.scene.isDirty)
                return true;
            if (Application.isBatchMode)
                return false;
            int choice = EditorUtility.DisplayDialogComplex("Flexible UI",
                F("\"{0}\" has unsaved changes. Save them before opening \"{1}\"?", System.IO.Path.GetFileNameWithoutExtension(stage.assetPath), System.IO.Path.GetFileNameWithoutExtension(nextPath)),
                T("Save"), T("Cancel"), T("Don't save"));
            if (choice == 1)
                return false;
            if (choice == 2)
            {
                stage.ClearDirtiness();
                return true;
            }
            if (IsReady(stage, out _))
                return Save(stage);
            bool saved = PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath) != null;
            if (saved)
                stage.ClearDirtiness();
            return saved;
        }

        // 한 화면의 기록을 모두 지운다 (그 화면은 기준 화면과 같아진다)
        public static bool ClearProfile(PrefabStage stage, string profileId)
        {
            return Commit(stage, GetActiveProfileId(stage), layout =>
            {
                foreach (FlexibleLayoutEntry entry in layout.Entries)
                    entry.states.RemoveAll(s => s.profileId == profileId);
                // 안쪽 프리팹으로 옮겨진(Apply된) 오브젝트의 기록은 이제 안쪽 프리팹이 맡으므로 지운다
            layout.Entries.RemoveAll(e => e.target == null || e.states.Count == 0 || FlexiblePathUtility.IsOwnedByNested(e.target, layout.transform));
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
            // 바뀐 게 없으면 화면만 바꾸고 파일은 다시 쓰지 않는다 (같은 화면 저장·기록 수정은 늘 저장)
            bool needSave = stage.scene.isDirty || modify != null || viewAfter == active;
            var root = (RectTransform)stage.prefabContentsRoot.transform;
            var kept = new List<FlexibleGridPlacement>(session.placements);
            bool ok;

            busy = true;
            try
            {
                // 편집용으로만 켜고 끈 것은 원래대로 돌려서 기록·저장한다
                HideView(session);
                if (active != baseId)
                {
                    string before = needSave ? null : RecordsOf(layout, active);
                    Record(stage, layout, session, active);
                    needSave |= before != null && before != RecordsOf(layout, active);
                    RestoreBase(stage, session);
                }
                else
                {
                    bool fitsChanged = RecordBaseFits(layout, session, baseId);
                    // 「바뀜」 표시 없이 스크립트로 바뀐 값도 놓치지 않게 저장된 프리팹과 비교한다
                    if (!needSave)
                        needSave = fitsChanged || DiffersFromSaved(stage, session);
                }

                modify?.Invoke(layout);
                ok = !needSave || InvokeSave(stage);
                if (ok && needSave)
                    SaveCount++;
                diskMap = null;
                fullDiskMap = null;

                // 이제 내용 = 저장된 기준 배치
                RebuildLinks(session, root);
                session.placements.Clear();
                if (viewAfter == baseId)
                {
                    session.placements.AddRange(active == baseId ? kept : session.basePlacements);
                    RestoreBasePlacements(layout, session, baseId);
                }
                else
                {
                    if (active == baseId)
                        session.basePlacements = kept;
                    ApplyProfileStates(layout, session, viewAfter);
                    ApplyNested(stage, viewAfter);
                }
                session.profileId = viewAfter;
                SessionState.SetString(ViewKeyPrefix + stage.assetPath, viewAfter);
                session.placements.RemoveAll(p => p.target == null);
                ShowView(session);
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
            // 내용은 이제 저장된 것(또는 방금 저장한 것)과 같다. 화면 크기가 바뀐 뒤 레이아웃·글자 컴포넌트가 다시 계산되며 붙는
            // 「변경 있음」 표시를 몇 프레임 동안 지운다 (남아 있으면 다음 화면 전환이 바뀐 게 없어도 파일을 다시 저장한다)
            if (ok)
                KeepClean(stage, false, 10);
            // 화면 모양이 바뀐 뒤 Scene 뷰를 프리팹에 맞춘다 (모양은 Game 뷰 크기가 바뀐 다음 프레임에 바뀐다)
            if (active != viewAfter)
                frameAfterSwitch = stage.assetPath;
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
            HideView(session);
            reshowView = true;
            if (active == layout.Settings.BaseProfileId)
            {
                RecordBaseFits(layout, session, active);
                return;
            }

            Record(stage, layout, session, active);
            RestoreBase(stage, session);
            reapplyProfile = active;
        }

        private static void OnSaved(GameObject root)
        {
            if (busy)
                return;

            diskMap = null;
            fullDiskMap = null;
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
                ApplyNested(stage, reapplyProfile);
                reapplyProfile = null;
                // 다시 적용한 값은 이미 저장된 화면 기록이라 저장할 변경이 아니다
                stage.ClearDirtiness();
                EditorApplication.delayCall += () =>
                {
                    if (stage != null && stage.prefabContentsRoot != null)
                        stage.ClearDirtiness();
                };
            }
            if (reshowView)
            {
                reshowView = false;
                ShowView(session);
                stage.ClearDirtiness();
            }
            Changed?.Invoke();
        }

        // 지금 화면(기준 화면이 아닐 때)에서 기준 배치와 달라 저장하면 이 화면의 기록이 될 블록 (저장 전 변경 포함).
        // 편집용 보기로만 켜고 끈 것은 세지 않는다
        public static List<RectTransform> ChangedBlocks(PrefabStage stage)
        {
            var result = new List<RectTransform>();
            FlexibleWorkspace session = GetSession(stage);
            if (session == null || IsBaseView(stage))
                return result;
            foreach (FlexibleWorkspaceLink link in session.links)
            {
                if (link.target == null)
                    continue;
                RectTransform saved = FindSavedByPath(stage, link.sourcePath);
                if (saved == null)
                    continue;
                if (FlexibleWorkspaceService.TryBuildState(link.target, saved, null, out FlexibleProfileState state, out _))
                {
                    bool viewOnly = session.FindView(link.target.gameObject) != null && state.controlActive && !state.controlLayoutGroup && !state.controlSizeFitter
                        && state.rect.Approximately(FlexibleRectState.Capture(saved), 0.01f);
                    if (!viewOnly)
                        result.Add(link.target);
                }
                else if (session.FindPlacement(link.target) != null)
                {
                    result.Add(link.target);
                }
            }
            return result;
        }

        // ───────── 편집용 보기 (저장되지 않음) ─────────

        // 편집하는 동안만 켜거나 끈다. 저장할 때는 원래 상태로 저장된다
        public static void SetViewVisible(PrefabStage stage, GameObject target, bool visible)
        {
            FlexibleWorkspace session = Prepare(stage);
            if (session == null || target == null)
                return;
            bool wasDirty = stage.scene.isDirty;
            FlexibleViewOverride view = session.FindView(target);
            if (view == null)
            {
                if (FlexibleViewOverride.IsVisible(target) == visible)
                    return;
                Canvas canvas = target.GetComponent<Canvas>();
                view = new FlexibleViewOverride { target = target, savedActive = target.activeSelf, savedCanvas = canvas == null || canvas.enabled };
                session.viewOverrides.Add(view);
            }
            view.shown = visible;
            ApplyShown(view);
            // 원래 보이던 그대로가 되면 따로 기억할 필요가 없다
            if (view.ExpectedActive == view.savedActive && view.ExpectedCanvas == view.savedCanvas)
                session.viewOverrides.Remove(view);
            KeepClean(stage, wasDirty);
            Changed?.Invoke();
            SceneView.RepaintAll();
        }

        // 켜고 끈 다음 프레임에 레이아웃이 다시 계산되며 「변경 있음」이 붙는 것을 지운다 (원래 깨끗했을 때만)
        private static void KeepClean(PrefabStage stage, bool wasDirty, int frames = 2)
        {
            if (wasDirty)
                return;
            stage.ClearDirtiness();
            EditorApplication.CallbackFunction clear = null;
            clear = () =>
            {
                if (stage != null && stage.prefabContentsRoot != null)
                    stage.ClearDirtiness();
                if (--frames <= 0)
                    EditorApplication.update -= clear;
            };
            EditorApplication.update += clear;
        }

        // 편집용 보기를 모두 원래대로 (keepRoot면 꺼진 채 저장된 맨 위는 계속 켜 둔다)
        public static void ClearView(PrefabStage stage, bool keepRoot)
        {
            FlexibleWorkspace session = GetSession(stage);
            if (session == null)
                return;
            bool wasDirty = stage.scene.isDirty;
            GameObject root = stage.prefabContentsRoot;
            foreach (FlexibleViewOverride view in session.viewOverrides.ToList())
            {
                if (keepRoot && view.target == root)
                    continue;
                if (view.target != null)
                    ApplySaved(view);
                session.viewOverrides.Remove(view);
            }
            if (!wasDirty)
                stage.ClearDirtiness();
            Changed?.Invoke();
            SceneView.RepaintAll();
        }

        public static FlexibleViewOverride FindView(PrefabStage stage, GameObject target)
        {
            FlexibleWorkspace session = GetSession(stage);
            return session != null ? session.FindView(target) : null;
        }

        // GetViewByPath로 받아 둔 보기를 다시 연 프리팹에 되살린다 (없어진 경로는 건너뛴다)
        public static void RestoreViewByPath(PrefabStage stage, Dictionary<string, bool> view)
        {
            if (stage == null || view == null)
                return;
            Transform root = stage.prefabContentsRoot.transform;
            foreach (KeyValuePair<string, bool> pair in view)
            {
                Transform target = FlexiblePathUtility.FindByPath(root, pair.Key);
                if (target != null)
                    SetViewVisible(stage, target.gameObject, pair.Value);
            }
        }

        // 미리보기·검수 사본에 같은 보기를 적용하기 위한 경로별 값 (루트는 "")
        public static Dictionary<string, bool> GetViewByPath(PrefabStage stage)
        {
            var result = new Dictionary<string, bool>();
            FlexibleWorkspace session = GetSession(stage);
            if (session == null)
                return result;
            Transform root = stage.prefabContentsRoot.transform;
            foreach (FlexibleViewOverride view in session.viewOverrides)
            {
                if (view.target != null)
                    result[view.target.transform == root ? "" : FlexiblePathUtility.GetPath(root, view.target.transform)] = view.shown;
            }
            return result;
        }

        private static void HideView(FlexibleWorkspace session)
        {
            session.viewOverrides.RemoveAll(v => v.target == null);
            // 편집용으로 바꾼 뒤 사용자가 직접 켜고 끈 것은 진짜 변경으로 보고 그대로 둔다
            session.viewOverrides.RemoveAll(v => v.target.activeSelf != v.ExpectedActive || (v.Canvas != null && v.Canvas.enabled != v.ExpectedCanvas));
            foreach (FlexibleViewOverride view in session.viewOverrides)
                ApplySaved(view);
        }

        // 지금 상태(= 저장된 상태)를 원래 값으로 다시 기억하고 편집용 값을 적용한다
        private static void ShowView(FlexibleWorkspace session)
        {
            session.viewOverrides.RemoveAll(v => v.target == null);
            foreach (FlexibleViewOverride view in session.viewOverrides)
            {
                view.savedActive = view.target.activeSelf;
                view.savedCanvas = view.Canvas == null || view.Canvas.enabled;
                ApplyShown(view);
            }
        }

        private static void ApplyShown(FlexibleViewOverride view)
        {
            view.target.SetActive(view.ExpectedActive);
            if (view.Canvas != null)
                view.Canvas.enabled = view.ExpectedCanvas;
        }

        private static void ApplySaved(FlexibleViewOverride view)
        {
            view.target.SetActive(view.savedActive);
            if (view.Canvas != null)
                view.Canvas.enabled = view.savedCanvas;
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
                FlexibleGridPlacement placement = session.FindPlacement(link.target);
                if (!FlexibleWorkspaceService.TryBuildState(link.target, saved, profileId, out FlexibleProfileState state, out LayoutGroup savedGroup))
                {
                    // 칸에 놓은 결과가 기준 배치와 똑같아도 칸 설정(칸·모드·SafeArea)은 남긴다
                    if (placement == null)
                        continue;
                    state = new FlexibleProfileState
                    {
                        profileId = profileId,
                        rect = FlexibleRectState.Capture(link.target),
                        active = link.target.gameObject.activeSelf,
                    };
                    savedGroup = null;
                }

                FlexibleWorkspaceService.MarkGridPlacement(state, placement);
                FlexibleLayoutEntry target = layout.Entries.Find(e => e.target == link.target);
                if (target == null)
                {
                    target = new FlexibleLayoutEntry { target = link.target };
                    layout.Entries.Add(target);
                }
                if (savedGroup != null)
                    target.layoutGroup = link.target.GetComponent<LayoutGroup>();
                if (state.controlSizeFitter)
                    target.sizeFitter = link.target.GetComponent<ContentSizeFitter>();
                target.states.Add(state);
            }

            // 안쪽 프리팹으로 옮겨진(Apply된) 오브젝트의 기록은 이제 안쪽 프리팹이 맡으므로 지운다
            layout.Entries.RemoveAll(e => e.target == null || e.states.Count == 0 || FlexiblePathUtility.IsOwnedByNested(e.target, layout.transform));
            layout.Roles.RemoveAll(r => r.target == null || string.IsNullOrEmpty(r.role));
        }

        // 기준 화면에서 칸에 맞춰 줄인 블록. 기준 화면은 프리팹 자체라 줄이기 정보(칸 영역·최대 배율)를 따로 기록해야
        // 실행 중·미리보기에서 화면 크기(같은 세로 안의 다른 비율)에 맞춰 다시 줄인다. 바뀌었으면 true
        internal static bool RecordBaseFits(FlexibleLayout layout, FlexibleWorkspace session, string baseId)
        {
            string before = RecordsOf(layout, baseId);
            foreach (FlexibleLayoutEntry entry in layout.Entries)
                entry.states.RemoveAll(s => s.profileId == baseId);
            foreach (FlexibleGridPlacement placement in session.placements)
            {
                if (placement.target == null || !placement.fit)
                    continue;
                var state = new FlexibleProfileState
                {
                    profileId = baseId,
                    rect = FlexibleRectState.Capture(placement.target),
                    active = placement.target.gameObject.activeSelf,
                };
                FlexibleWorkspaceService.MarkGridPlacement(state, placement);
                FlexibleLayoutEntry entry = layout.Entries.Find(e => e.target == placement.target);
                if (entry == null)
                {
                    entry = new FlexibleLayoutEntry { target = placement.target };
                    layout.Entries.Add(entry);
                }
                entry.states.Add(state);
            }
            // 안쪽 프리팹으로 옮겨진(Apply된) 오브젝트의 기록은 이제 안쪽 프리팹이 맡으므로 지운다
            layout.Entries.RemoveAll(e => e.target == null || e.states.Count == 0 || FlexiblePathUtility.IsOwnedByNested(e.target, layout.transform));
            return before != RecordsOf(layout, baseId);
        }

        // 기록해 둔 기준 화면의 칸 배치를 되살린다 (프리팹을 다시 열어도 칸·줄이기 설정이 남게)
        internal static void RestoreBasePlacements(FlexibleLayout layout, FlexibleWorkspace session, string baseId)
        {
            foreach (FlexibleLayoutEntry entry in layout.Entries)
            {
                FlexibleProfileState state = entry.target != null ? entry.Find(baseId) : null;
                if (state == null || !state.gridOrigin || session.FindPlacement(entry.target) != null)
                    continue;
                session.placements.Add(FlexibleWorkspaceService.FromState(entry.target, state, state.gridResult));
            }
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
            RestoreRoot(stage);
            RestoreNested(stage);
        }

        // 프리팹 루트의 자리는 그것을 담은 쪽(씬·바깥 프리팹)이 정한다. 세로가 아닌 화면에서 옮긴 값이 세로 기준으로 저장되지 않게 되돌린다
        private static void RestoreRoot(PrefabStage stage)
        {
            GameObject root = stage.prefabContentsRoot;
            var rootRect = root.transform as RectTransform;
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(stage.assetPath);
            if (rootRect == null || asset == null || !(asset.transform is RectTransform savedRect) || root.GetComponent<Canvas>() != null)
                return;

            FlexibleRectState saved = FlexibleRectState.Capture(savedRect);
            FlexibleRectState now = FlexibleRectState.Capture(rootRect);
            // 내용에 맞춰 크기가 정해지는 루트는 크기 차이를 사용자의 변경으로 보지 않는다
            if (root.GetComponent<ContentSizeFitter>() != null || root.GetComponent<LayoutGroup>() != null)
                now.sizeDelta = saved.sizeDelta;
            if (now.Approximately(saved, 0.01f))
                return;
            saved.ApplyTo(rootRect);
            SceneView.lastActiveSceneView?.ShowNotification(new GUIContent(T("The root of this prefab is placed by whatever holds it, so moving it on this screen was not saved.")), 4.0);
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

            var savedFitter = saved.GetComponent<ContentSizeFitter>();
            var fitter = target.GetComponent<ContentSizeFitter>();
            if (savedFitter != null && fitter != null)
            {
                fitter.horizontalFit = savedFitter.horizontalFit;
                fitter.verticalFit = savedFitter.verticalFit;
            }
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
                if (!state.fit)
                    result.localScale = baseScale;
                session.placements.Add(FlexibleWorkspaceService.FromState(entry.target, state, result));
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

        // 안쪽 프리팹(자기 FlexibleLayout을 가진 중첩 프리팹)도 지금 화면으로 보여 준다.
        // 보기 전용이라 바깥 프리팹 기록에 들어가지 않고(경로 목록에서 빠짐), 저장 전에 RestoreNested로 되돌린다
        private static void ApplyNested(PrefabStage stage, string profileId)
        {
            foreach (FlexibleLayout nested in NestedLayouts(stage))
            {
                nested.RecaptureBase();
                nested.ApplyForScreen(profileId);
            }
        }

        private static void RestoreNested(PrefabStage stage)
        {
            Transform root = stage.prefabContentsRoot.transform;
            foreach (FlexibleLayout nested in NestedLayouts(stage))
            {
                foreach (FlexibleLayoutEntry entry in nested.Entries)
                {
                    if (entry.target == null)
                        continue;
                    RectTransform saved = FindSavedAnywhere(stage, FlexiblePathUtility.GetPath(root, entry.target));
                    if (saved != null)
                        ApplySaved(saved, entry.target);
                }
            }
        }

        private static IEnumerable<FlexibleLayout> NestedLayouts(PrefabStage stage)
        {
            GameObject root = stage.prefabContentsRoot;
            return root.GetComponentsInChildren<FlexibleLayout>(true).Where(l => l.gameObject != root);
        }

        // 안쪽 프리팹 내부까지 포함해 저장된 프리팹에서 같은 경로의 오브젝트를 찾는다
        private static RectTransform FindSavedAnywhere(PrefabStage stage, string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(stage.assetPath);
            if (asset == null || !(asset.transform is RectTransform assetRoot))
                return null;

            if (fullDiskMap == null || fullDiskMapAsset != asset)
            {
                fullDiskMap = new Dictionary<string, RectTransform>();
                foreach (RectTransform rt in assetRoot.GetComponentsInChildren<RectTransform>(true))
                    fullDiskMap[FlexiblePathUtility.GetPath(assetRoot, rt)] = rt;
                fullDiskMapAsset = asset;
            }
            return fullDiskMap.TryGetValue(path, out RectTransform saved) && saved != null ? saved : null;
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

            // 맨 위가 꺼진 채(오브젝트 또는 Canvas) 저장된 프리팹(실행 중에 코드가 켬)은 편집하는 동안만 켜서 보여 준다. 저장은 꺼진 그대로
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(stage.assetPath);
            bool savedActive = asset != null ? asset.activeSelf : root.activeSelf;
            Canvas savedRootCanvas = asset != null ? asset.GetComponent<Canvas>() : root.GetComponent<Canvas>();
            bool savedCanvas = savedRootCanvas == null || savedRootCanvas.enabled;
            if (!savedActive || !savedCanvas)
            {
                var view = new FlexibleViewOverride { target = root, savedActive = savedActive, savedCanvas = savedCanvas, shown = true };
                session.viewOverrides.Add(view);
                ApplyShown(view);
            }
            KeepClean(stage, wasDirty);
            return session;
        }

        // 다음에 이 프리팹을 열 때 이 화면으로 연다 (바깥·안쪽 프리팹을 오갈 때 보던 화면을 이어 간다)
        public static void OpenNextOn(string assetPath, string profileId)
        {
            if (string.IsNullOrEmpty(profileId))
                SessionState.EraseString(OpenOnKeyPrefix + assetPath);
            else
                SessionState.SetString(OpenOnKeyPrefix + assetPath, profileId);
        }

        private static void OnOpened(PrefabStage stage)
        {
            EnsureEnvironmentScaler(stage);
            SessionState.EraseString(ViewKeyPrefix + stage.assetPath);
            string openOn = SessionState.GetString(OpenOnKeyPrefix + stage.assetPath, "");
            SessionState.EraseString(OpenOnKeyPrefix + stage.assetPath);
            FlexibleWorkspace opened = Prepare(stage);
            if (opened == null)
                return;
            if (IsReady(stage, out FlexibleLayout openedLayout))
                RestoreBasePlacements(openedLayout, opened, openedLayout.Settings.BaseProfileId);

            if (!string.IsNullOrEmpty(openOn))
                SwitchOnOpen(stage, openOn);
            if (AutoGameView)
                SelectWorkResolution(GetActiveProfile(stage));
            frameAfterSwitch = stage.assetPath;
            CleanIfUnchangedLater(stage);
            Changed?.Invoke();
        }

        private const string CleanBeforeReloadKey = "FlexibleUITool.CleanBeforeReload";

        // 연 직후·스크립트를 다시 불러온 직후에는 아무것도 바꾸지 않았는데 「변경 있음」이 붙는 경우가 있다(글자 컴포넌트·레이아웃 재계산).
        // 몇 프레임 뒤 기준 화면이고 저장된 프리팹과 실제로 같으면 표시를 지운다 (닫을 때 저장 확인창이 뜨지 않게)
        private static void CleanIfUnchangedLater(PrefabStage stage)
        {
            int frames = 10;
            EditorApplication.CallbackFunction check = null;
            check = () =>
            {
                if (--frames > 0)
                    return;
                EditorApplication.update -= check;
                if (stage == null || stage.prefabContentsRoot == null || !stage.scene.isDirty || busy || !IsBaseView(stage))
                    return;
                FlexibleWorkspace session = GetSession(stage);
                if (session != null && !DiffersFromSaved(stage, session))
                    stage.ClearDirtiness();
            };
            EditorApplication.update += check;
        }

        // 방금 연 프리팹(내용 = 저장된 기준 배치)을 저장하지 않고 다른 화면으로 바꾼다
        private static void SwitchOnOpen(PrefabStage stage, string profileId)
        {
            if (!IsReady(stage, out FlexibleLayout layout))
                return;
            FlexibleProfileSettings settings = layout.Settings;
            FlexibleWorkspace session = Prepare(stage);
            if (settings.Find(profileId) == null || profileId == settings.BaseProfileId || session.profileId != settings.BaseProfileId)
                return;

            bool wasDirty = stage.scene.isDirty;
            busy = true;
            try
            {
                HideView(session);
                session.basePlacements = new List<FlexibleGridPlacement>(session.placements);
                session.placements.Clear();
                ApplyProfileStates(layout, session, profileId);
                ApplyNested(stage, profileId);
                session.profileId = profileId;
                SessionState.SetString(ViewKeyPrefix + stage.assetPath, profileId);
                session.placements.RemoveAll(p => p.target == null);
                ShowView(session);
                ClearUndo((RectTransform)stage.prefabContentsRoot.transform, session);
                KeepClean(stage, wasDirty);
            }
            finally
            {
                busy = false;
            }
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
            if (busy || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                return;

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            bool matching = IsShapeMatching(stage, out FlexibleProfile profile);
            // 화면을 바꾼 뒤 모양이 맞춰지면 Scene 뷰를 프리팹 전체에 맞춘다
            if (frameAfterSwitch != null && stage != null && stage.assetPath == frameAfterSwitch && (matching || !AutoGameView))
            {
                frameAfterSwitch = null;
                FrameStage(stage);
            }
            if (AutoGameView && !matching)
                SelectWorkResolution(profile);
        }

        // Scene 뷰를 열린 프리팹 전체가 보이게 맞춘다
        public static void FrameStage(PrefabStage stage)
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null || stage == null || !(stage.prefabContentsRoot.transform is RectTransform root))
                return;
            var corners = new Vector3[4];
            root.GetWorldCorners(corners);
            var bounds = new Bounds(corners[0], Vector3.zero);
            foreach (Vector3 corner in corners)
                bounds.Encapsulate(corner);
            if (bounds.size.sqrMagnitude > 0f)
                view.Frame(bounds, false);
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
            if (environment == null || environment.GetComponent<Canvas>() == null)
                return;
            CanvasScaler existing = environment.GetComponent<CanvasScaler>();
            // 이 툴이 붙인 스케일러만 다시 맞춘다 (사용자의 편집 환경에 있던 스케일러는 그대로)
            var own = existing as FlexibleCanvasScaler;
            if (existing != null && (own == null || (own.hideFlags & HideFlags.DontSave) == 0))
                return;

            FlexibleProfileSettings settings = FlexibleSettingsUtility.GetOrCreateDefault();
            FlexibleProfile baseProfile = settings.Find(settings.BaseProfileId);
            Vector2 reference = baseProfile != null ? baseProfile.workResolution : new Vector2(1080, 1920);

            FlexibleCanvasScaler scaler = own;
            if (scaler == null)
            {
                scaler = environment.gameObject.AddComponent<FlexibleCanvasScaler>();
                scaler.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            }
            // 이 프리팹을 실제로 그리는 캔버스와 같은 단위로 편집한다 (모르면 프로젝트 기준)
            FlexibleCanvasRule rule = FlexibleSettingsUtility.FindCanvasRule(root, settings);
            if (!rule.valid)
                rule = new FlexibleCanvasRule { valid = true, reference = reference, scaleFactor = 1f, fallbackDpi = 96f };
            // Scene 뷰가 그릴 때마다 불리므로 규칙이 달라졌을 때만 고친다
            if (own == null || !scaler.Rule.SameAs(rule))
                scaler.UseRule(rule, settings);
        }
    }
}
