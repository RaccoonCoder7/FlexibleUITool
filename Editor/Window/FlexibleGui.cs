using UnityEditor;
using UnityEngine;

namespace FlexibleUITool.EditorTools
{
    // 창 공용 그리기: 튜토리얼 캐릭터·말풍선, 눌러야 할 곳 강조, 큰 버튼 스타일
    public static class FlexibleGui
    {
        // 튜토리얼이 지금 가리키는 컨트롤 id (없으면 null)
        public static string Highlight { get; set; }

        private static Texture2D mascot;
        private static GUIStyle bubble;
        private static GUIStyle bigButton;
        private static GUIStyle bigButtonOn;
        private static GUIStyle wrapLabel;
        private static GUIStyle wrapTextArea;
        private static GUIStyle title;
        private static GUIStyle headerLabel;

        // 너구리 (16x16, 위에서 아래로). . 투명 k 외곽 g 털 l 흰색 m 마스크 c 볼 띠 e 동공·코 w 눈빛
        // y 노랑(귀·눈) v 보라(눈) p 속귀 r 볼터치 h 앞머리
        private static readonly string[] MascotPixels =
        {
            "................",
            "..kk........kk..",
            ".kpyk......kypk.",
            ".kyykkkhhkkkyyk.",
            "kgggggghhggggggk",
            "kggllggggggllggk",
            "kmmmmmllllmmmmmk",
            "kmmwvmllllmwvmmk",
            "kmmyemllllmeymmk",
            "kgcccllllllcccgk",
            "kgrrlllkklllrrgk",
            "kggllllllllllggk",
            ".kggllllllllggk.",
            "..kkggggggggkk..",
            "....kkkkkkkk....",
            "................",
        };

        public static Texture2D Mascot
        {
            get
            {
                if (mascot != null)
                    return mascot;

                mascot = new Texture2D(16, 16, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    mascot.SetPixel(x, 15 - y, PixelColor(MascotPixels[y][x]));
                mascot.Apply();
                return mascot;
            }
        }

        public static GUIStyle Bubble => bubble ??= new GUIStyle(EditorStyles.helpBox)
        {
            fontSize = 12,
            wordWrap = true,
            richText = true,
            padding = new RectOffset(10, 10, 8, 8),
        };

        public static GUIStyle BigButton => bigButton ??= new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fixedHeight = 0f,
            wordWrap = true,
            richText = true,
            padding = new RectOffset(6, 6, 6, 6),
        };

        public static GUIStyle BigButtonOn
        {
            get
            {
                if (bigButtonOn != null)
                    return bigButtonOn;
                bigButtonOn = new GUIStyle(BigButton) { fontStyle = FontStyle.Bold };
                bigButtonOn.normal = bigButtonOn.onNormal;
                bigButtonOn.hover = bigButtonOn.onHover;
                return bigButtonOn;
            }
        }

        public static GUIStyle WrapLabel => wrapLabel ??= new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true };

        public static GUIStyle WrapTextArea => wrapTextArea ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };

        public static GUIStyle Title => title ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };

        public static GUIStyle HeaderLabel => headerLabel ??= new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            fixedHeight = 0f,
            margin = new RectOffset(4, 4, 0, 0),
        };

        public static Color Accent => EditorGUIUtility.isProSkin ? new Color(0.35f, 0.7f, 1f) : new Color(0.1f, 0.45f, 0.85f);

        public static Color Good => EditorGUIUtility.isProSkin ? new Color(0.45f, 0.85f, 0.5f) : new Color(0.15f, 0.55f, 0.2f);

        // 캐릭터와 말풍선. 말풍선 아래에 버튼을 놓을 수 있게 세로 영역을 열어 둔다 (EndMascot으로 닫는다)
        public static void BeginMascot(string text)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(GUILayout.Width(52f));
            GUILayout.Space(4f);
            Rect icon = GUILayoutUtility.GetRect(52f, 52f, GUILayout.Width(52f), GUILayout.Height(52f));
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(icon, Mascot, ScaleMode.ScaleToFit, true);
            EditorGUILayout.EndVertical();
            EditorGUILayout.BeginVertical();
            GUILayout.Label(text, Bubble);
        }

        public static void EndMascot()
        {
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        // 바로 앞에 그린 컨트롤이 튜토리얼이 가리키는 것이면 깜빡이는 테두리를 그린다
        // 마지막으로 강조한 컨트롤의 위치 (그린 곳의 좌표계 기준, 창이 그곳까지 스크롤할 때 쓴다)
        public static Rect MarkedRect { get; private set; }
        public static string MarkedId { get; private set; }

        // 창이 그리기 시작할 때 부른다 (지난번에 그린 위치를 쓰지 않도록)
        public static void ForgetMarked()
        {
            MarkedId = null;
        }

        public static void Mark(string id)
        {
            if (Highlight != id || Event.current.type != EventType.Repaint)
                return;
            Rect rect = GUILayoutUtility.GetLastRect();
            MarkedRect = rect;
            MarkedId = id;
            DrawFrame(rect);
        }

        public static void MarkRect(string id, Rect rect)
        {
            if (Highlight == id && Event.current.type == EventType.Repaint)
                DrawFrame(rect);
        }

        public static bool IsMarked(string id) => Highlight == id;

        private static void DrawFrame(Rect rect)
        {
            float pulse = 0.55f + 0.45f * Mathf.Sin((float)EditorApplication.timeSinceStartup * 6f);
            var color = new Color(1f, 0.6f, 0.1f, pulse);
            rect = new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f);
            const float t = 3f;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, t), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - t, rect.width, t), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, t, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - t, rect.y, t, rect.height), color);
        }

        // 언어마다 라벨 길이가 달라서 가장 긴 라벨에 맞춘다
        public static float FitLabels(params string[] labels)
        {
            float width = 64f;
            foreach (string label in labels)
                width = Mathf.Max(width, EditorStyles.label.CalcSize(new GUIContent(label)).x + 6f);
            return width;
        }

        public static void Section(string text)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }

        private static Color PixelColor(char c)
        {
            switch (c)
            {
                case 'k': return Hex(0x26242c);
                case 'g': return Hex(0x5d5966);
                case 'l': return Hex(0xf4f4f4);
                case 'm': return Hex(0x16161b);
                case 'c': return Hex(0x7c7f8a);
                case 'e': return Hex(0x0c0c10);
                case 'w': return Color.white;
                case 'y': return Hex(0xe9c53a);
                case 'v': return Hex(0xa77fe6);
                case 'p': return Hex(0xf2a6b6);
                case 'r': return Hex(0xf3a0b4);
                case 'h': return Hex(0xf7d3df);
                default: return new Color(0f, 0f, 0f, 0f);
            }
        }

        private static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
        }
    }
}
