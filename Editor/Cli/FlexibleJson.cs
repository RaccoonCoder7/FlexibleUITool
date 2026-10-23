using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FlexibleUITool.EditorTools
{
    // 결과 JSON 작성기 (JsonUtility는 생략 가능한 필드·가변 구조를 표현하지 못해 직접 쓴다)
    public sealed class FlexibleJson
    {
        private readonly StringBuilder builder = new StringBuilder();
        private readonly Stack<bool> isFirst = new Stack<bool>();

        public FlexibleJson BeginObject(string key = null)
        {
            Key(key);
            builder.Append('{');
            isFirst.Push(true);
            return this;
        }

        public FlexibleJson EndObject()
        {
            isFirst.Pop();
            builder.Append('}');
            return this;
        }

        public FlexibleJson BeginArray(string key = null)
        {
            Key(key);
            builder.Append('[');
            isFirst.Push(true);
            return this;
        }

        public FlexibleJson EndArray()
        {
            isFirst.Pop();
            builder.Append(']');
            return this;
        }

        public FlexibleJson Value(string key, string value)
        {
            Key(key);
            if (value == null)
                builder.Append("null");
            else
                AppendString(value);
            return this;
        }

        public FlexibleJson Value(string key, bool value)
        {
            Key(key);
            builder.Append(value ? "true" : "false");
            return this;
        }

        public FlexibleJson Value(string key, int value)
        {
            Key(key);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public FlexibleJson Value(string key, float value)
        {
            Key(key);
            AppendNumber(value);
            return this;
        }

        public FlexibleJson Numbers(string key, params float[] values)
        {
            BeginArray(key);
            foreach (float value in values)
            {
                Key(null);
                AppendNumber(value);
            }
            return EndArray();
        }

        public FlexibleJson Numbers(string key, params int[] values)
        {
            BeginArray(key);
            foreach (int value in values)
                Value(null, value);
            return EndArray();
        }

        public FlexibleJson Strings(string key, IEnumerable<string> values)
        {
            BeginArray(key);
            foreach (string value in values)
                Value(null, value);
            return EndArray();
        }

        // 다른 작성기로 따로 만든 "키":값 조각을 그대로 넣는다
        public FlexibleJson Raw(string keyAndValue)
        {
            Key(null);
            builder.Append(keyAndValue);
            return this;
        }

        public override string ToString()
        {
            return builder.ToString();
        }

        private void Key(string key)
        {
            if (isFirst.Count > 0)
            {
                if (!isFirst.Peek())
                    builder.Append(',');
                isFirst.Pop();
                isFirst.Push(false);
            }

            if (key == null)
                return;

            AppendString(key);
            builder.Append(':');
        }

        private void AppendNumber(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                builder.Append("null");
            else
                builder.Append(value.ToString("0.#####", CultureInfo.InvariantCulture));
        }

        private void AppendString(string value)
        {
            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
        }
    }
}
