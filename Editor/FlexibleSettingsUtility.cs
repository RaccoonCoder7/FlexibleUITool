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
