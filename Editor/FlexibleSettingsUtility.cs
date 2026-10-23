using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FlexibleUITool.EditorTools
{
    // 툴 위치(Assets 안 또는 UPM 패키지)와 프로젝트 데이터 위치, 공용 프로필 설정 애셋 조회·생성
    public static class FlexibleSettingsUtility
    {
        public const string PackageName = "com.raccooncoder7.flexibleuitool";

        // 프로젝트마다 생기는 데이터(설정 애셋·작업 프리팹). 패키지는 읽기 전용이라 항상 프로젝트 Assets 쪽
        public const string DataFolder = "Assets/FlexibleUITool";
        public const string SettingsPath = DataFolder + "/Settings/FlexibleProfileSettings.asset";

        private static string toolAssetPath;
        private static string toolFullPath;
        private static FlexibleProfileSettings cachedSettings;

        // 애셋 검색은 한 번에 수 ms 걸려서 창이 그려질 때마다 하지 않는다. 애셋이 바뀌면 다시 찾는다
        static FlexibleSettingsUtility()
        {
            EditorApplication.projectChanged += () => cachedSettings = null;
        }

        // 툴 파일이 있는 곳: "Assets/FlexibleUITool" 또는 "Packages/com.raccooncoder7.flexibleuitool"
        public static string ToolAssetPath
        {
            get
            {
                Resolve();
                return toolAssetPath;
            }
        }

        // 툴 파일이 있는 실제 폴더 (git 패키지면 Library/PackageCache/... 의 절대 경로)
        public static string ToolFullPath
        {
            get
            {
                Resolve();
                return toolFullPath;
            }
        }

        public static bool IsPackage => ToolAssetPath.StartsWith("Packages/");

        public static UnityEditor.PackageManager.PackageInfo PackageInfo =>
            UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(FlexibleSettingsUtility).Assembly);

        public static FlexibleProfileSettings GetOrCreateDefault()
        {
            if (cachedSettings != null)
                return cachedSettings;
            cachedSettings = FindOrCreateDefault();
            return cachedSettings;
        }

        private static FlexibleProfileSettings FindOrCreateDefault()
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(FlexibleProfileSettings));
            if (guids.Length > 0)
            {
                // 프로젝트 데이터 폴더의 설정을 우선한다 (샘플에 딸린 설정보다)
                string path = guids.Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p == SettingsPath ? 0 : p.StartsWith("Assets/") ? 1 : 2).First();
                return AssetDatabase.LoadAssetAtPath<FlexibleProfileSettings>(path);
            }

            EnsureFolder(Path.GetDirectoryName(SettingsPath)?.Replace('\\', '/'));
            var settings = ScriptableObject.CreateInstance<FlexibleProfileSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssetIfDirty(settings);
            return settings;
        }

        public const string SamplePrefabName = "FlexibleSample";
        public const string SampleTargetFolder = DataFolder + "/Samples";

        // 튜토리얼 샘플 폴더 (FlexibleSample 프리팹이 있는 곳). 없으면 null
        public static string FindSampleFolder()
        {
            return AssetDatabase.FindAssets(SamplePrefabName + " t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileNameWithoutExtension(p) == SamplePrefabName && p.StartsWith("Assets/"))
                .Select(p => Path.GetDirectoryName(p)?.Replace('\\', '/'))
                .OrderBy(p => p == SampleTargetFolder ? 0 : 1)
                .FirstOrDefault();
        }

        [MenuItem("Tools/FlexibleUITool/Import Tutorial Sample", false, 50)]
        private static void ImportTutorialSampleMenu()
        {
            string folder = ImportTutorialSample(out _);
            if (folder == null)
                return;
            Object prefab = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/" + SamplePrefabName + ".prefab");
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
        }

        // 튜토리얼 샘플을 Assets/FlexibleUITool/Samples 로 가져온다 (Package Manager의 샘플 Import는 Assets/Samples/<이름>/<버전>/ 에 넣어서 쓰지 않는다).
        // 예전 방식으로 Assets/Samples 에 들어온 샘플은 이 폴더로 옮긴다. 샘플 폴더를 돌려주고, 툴이 Assets 안에 있는데 샘플이 없으면 null
        public static string ImportTutorialSample(out bool imported)
        {
            imported = false;
            string existing = FindSampleFolder();
            if (existing == SampleTargetFolder || (existing != null && !IsPackage))
                return existing;

            EnsureFolder(SampleTargetFolder);
            if (existing != null)
            {
                MoveFolderContents(existing, SampleTargetFolder);
            }
            else
            {
                string source = Path.Combine(ToolFullPath, "Samples~", "Tutorial");
                if (!Directory.Exists(source))
                    return null;
                string target = Path.GetFullPath(SampleTargetFolder);
                foreach (string file in Directory.GetFiles(source))
                {
                    // .meta 도 같이 복사해 GUID(프리팹↔설정 연결)를 유지한다
                    string destination = Path.Combine(target, Path.GetFileName(file));
                    if (!File.Exists(destination))
                        File.Copy(file, destination);
                }
                AssetDatabase.Refresh();
                imported = true;
            }

            UseOneSettingsAsset(SampleTargetFolder);
            return SampleTargetFolder;
        }

        // 샘플에 딸린 설정 애셋을 프로젝트 설정으로 쓴다. 프로젝트 설정이 이미 있으면 샘플 프리팹을 그쪽에 연결하고 샘플 사본은 지운다
        private static void UseOneSettingsAsset(string sampleFolder)
        {
            string sampleSettings = sampleFolder + "/FlexibleProfileSettings.asset";
            if (AssetDatabase.LoadAssetAtPath<FlexibleProfileSettings>(sampleSettings) == null)
                return;

            var project = AssetDatabase.LoadAssetAtPath<FlexibleProfileSettings>(SettingsPath);
            if (project == null)
            {
                EnsureFolder(Path.GetDirectoryName(SettingsPath)?.Replace('\\', '/'));
                AssetDatabase.MoveAsset(sampleSettings, SettingsPath);
                return;
            }

            string prefabPath = sampleFolder + "/" + SamplePrefabName + ".prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var layout = root.GetComponent<FlexibleLayout>();
                if (layout != null)
                {
                    layout.Settings = project;
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.DeleteAsset(sampleSettings);
        }

        // 폴더 안의 애셋을 옮기고(GUID 유지), 비어 버린 상위 폴더는 Assets/Samples 까지 정리한다
        private static void MoveFolderContents(string from, string to)
        {
            foreach (string guid in AssetDatabase.FindAssets("", new[] { from }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(path)?.Replace('\\', '/') != from || AssetDatabase.IsValidFolder(path))
                    continue;
                string destination = to + "/" + Path.GetFileName(path);
                if (AssetDatabase.LoadMainAssetAtPath(destination) == null)
                    AssetDatabase.MoveAsset(path, destination);
            }

            for (string folder = from; folder != null && folder.StartsWith("Assets/Samples"); folder = Path.GetDirectoryName(folder)?.Replace('\\', '/'))
            {
                if (AssetDatabase.FindAssets("", new[] { folder }).Length > 0)
                    break;
                AssetDatabase.DeleteAsset(folder);
            }
        }

        public static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
                return;

            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        private static void Resolve()
        {
            if (toolAssetPath != null)
                return;

            UnityEditor.PackageManager.PackageInfo info = PackageInfo;
            if (info != null)
            {
                toolAssetPath = info.assetPath;
                toolFullPath = info.resolvedPath;
                return;
            }

            // Assets 안에 둔 경우: 이 스크립트(.../Editor/FlexibleSettingsUtility.cs)의 두 단계 위
            string script = AssetDatabase.FindAssets("FlexibleSettingsUtility t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileName(p) == "FlexibleSettingsUtility.cs");
            toolAssetPath = script != null
                ? Path.GetDirectoryName(Path.GetDirectoryName(script))?.Replace('\\', '/')
                : DataFolder;
            toolFullPath = Path.GetFullPath(toolAssetPath);
        }
    }
}
