using System;

namespace FlexibleUITool.EditorTools
{
    // CLI 명령 (flexui.ps1이 만드는 JSON과 1:1)
    [Serializable]
    public class FlexibleCommand
    {
        public string id;
        public string command;
        public string prefab;
        public string profile;
        public string[] presets;
        public string[] aspects;
        public string spec;
        public string output;
        public string folder;
        public string path;
        public string scene;
        public string role;
        public bool dryRun;
        public bool all;
    }

    // 배치 스펙: 프로필별로 블록(역할 또는 경로)을 어디에 둘지 적은 파일
    [Serializable]
    public class FlexibleSpec
    {
        public string prefab;
        public FlexibleSpecProfile[] profiles;
    }

    [Serializable]
    public class FlexibleSpecProfile
    {
        public string profile;
        public FlexibleSpecBlock[] blocks;
    }

    // path가 있으면 그 오브젝트 하나, 없으면 role이 붙은 모든 블록이 대상
    [Serializable]
    public class FlexibleSpecBlock
    {
        public string path;
        public string role;
        public int[] cell;
        public string modeX;
        public string modeY;
        public bool safeArea = true;
        public float[] offset;
        public float[] sizeOffset;
        public float[] scale;
        public FlexibleSpecRect rect;
        public string visible;
        public bool reset;

        public bool HasCell => cell != null && cell.Length == 4;
        public bool HasRect => rect != null && rect.IsValid;
        public string Label => !string.IsNullOrEmpty(path) ? path : $"role:{role}";
    }

    // 칸으로 표현할 수 없는 배치를 RectTransform 값 그대로 지정
    [Serializable]
    public class FlexibleSpecRect
    {
        public float[] anchorMin;
        public float[] anchorMax;
        public float[] pivot;
        public float[] position;
        public float[] size;

        public bool IsValid => Is2(anchorMin) && Is2(anchorMax) && Is2(pivot) && Is2(position) && Is2(size);

        private static bool Is2(float[] values)
        {
            return values != null && values.Length == 2;
        }
    }

    // 명령 실패 (code는 언어와 무관한 식별자, message는 영어)
    public class FlexibleCommandException : Exception
    {
        public string Code { get; }

        public FlexibleCommandException(string code, string message) : base(message)
        {
            Code = code;
        }
    }
}
