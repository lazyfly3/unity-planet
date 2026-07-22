using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpacecraftEditor
{
    public static class ThrusterKeyBinding
    {
        private static readonly KeyCode[] ReservedKeys =
        {
            KeyCode.Q,
            KeyCode.E,
            KeyCode.T,
            KeyCode.R,
            KeyCode.C,
            KeyCode.Escape,
            KeyCode.Delete,
            KeyCode.Backspace
        };

        private static readonly KeyCode[] bindableKeys = BuildBindableKeys();

        public static IReadOnlyList<KeyCode> BindableKeys => bindableKeys;

        public static bool IsBindable(KeyCode key)
        {
            if (key == KeyCode.None || IsReserved(key))
                return false;

            var name = key.ToString();
            if (name.StartsWith("Mouse", StringComparison.Ordinal) ||
                name.StartsWith("Joystick", StringComparison.Ordinal))
            {
                return false;
            }

            return Enum.IsDefined(typeof(KeyCode), key);
        }

        public static bool IsReserved(KeyCode key)
        {
            for (var index = 0; index < ReservedKeys.Length; index++)
            {
                if (ReservedKeys[index] == key)
                    return true;
            }
            return false;
        }

        public static string GetDisplayName(KeyCode key)
        {
            if (key == KeyCode.None)
                return "未绑定";

            var name = key.ToString();
            if (name.StartsWith("Alpha", StringComparison.Ordinal) && name.Length == 6)
                return name.Substring(5);
            if (name.StartsWith("Keypad", StringComparison.Ordinal) && name.Length == 7)
                return "小键盘 " + name.Substring(6);

            switch (key)
            {
                case KeyCode.Space: return "空格";
                case KeyCode.Return: return "回车";
                case KeyCode.KeypadEnter: return "小键盘回车";
                case KeyCode.Tab: return "Tab";
                case KeyCode.UpArrow: return "↑";
                case KeyCode.DownArrow: return "↓";
                case KeyCode.LeftArrow: return "←";
                case KeyCode.RightArrow: return "→";
                case KeyCode.LeftShift: return "左 Shift";
                case KeyCode.RightShift: return "右 Shift";
                case KeyCode.LeftControl: return "左 Ctrl";
                case KeyCode.RightControl: return "右 Ctrl";
                case KeyCode.LeftAlt: return "左 Alt";
                case KeyCode.RightAlt: return "右 Alt";
                default: return name;
            }
        }

        private static KeyCode[] BuildBindableKeys()
        {
            var result = new List<KeyCode>();
            var seen = new HashSet<int>();
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                if (IsBindable(key) && seen.Add((int)key))
                    result.Add(key);
            }
            return result.ToArray();
        }
    }
}
