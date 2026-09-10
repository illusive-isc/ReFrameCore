using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>VCC / ALCOM を「開くだけ」する。
    ///
    /// vcc: のリンクで指示できるのは一覧 (リポジトリ) の追加だけで、特定のパッケージを
    /// 更新させることはできない。そこでリンクは使わず、利用者が vcc: に紐付けている
    /// アプリの実行ファイルを見つけて、引数なしで起動する。紐付けが無ければ既定の
    /// 導入先を順に探す。ALCOM を使っている人は ALCOM が、VCC の人は VCC が開く。</summary>
    internal static class ReFrameVccLauncher
    {
        /// <summary>開ける見込みがあるか (ボタンを出すかどうかの判断に使う)。</summary>
        internal static bool IsAvailable => FindExecutable() != null;

        /// <summary>VCC か ALCOM を開く。開けなければ理由をダイアログで伝える。</summary>
        internal static void Open()
        {
            var path = FindExecutable();
            if (path == null)
            {
                EditorUtility.DisplayDialog(
                    "ReFrame の更新",
                    "VCC (または ALCOM) が見つかりませんでした。"
                        + (char)10
                        + "手動で開いて、Manage Packages から更新してください。",
                    "OK"
                );
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception error)
            {
                Debug.LogWarning("[ReFrameCore] VCC を開けませんでした: " + error.Message);
                EditorUtility.DisplayDialog(
                    "ReFrame の更新",
                    "VCC を開けませんでした。手動で開いてください。" + (char)10 + error.Message,
                    "OK"
                );
            }
        }

        /// <summary>起動する実行ファイルを決める。紐付けを優先し、無ければ既定の導入先を探す。</summary>
        static string FindExecutable()
        {
#if UNITY_EDITOR_WIN
            var registered = FromProtocolHandler();
            if (registered != null)
                return registered;

            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (
                var candidate in new[]
                {
                    Path.Combine(local, "ALCOM", "ALCOM.exe"),
                    Path.Combine(local, "Programs", "ALCOM", "ALCOM.exe"),
                    Path.Combine(local, "Programs", "VRChat Creator Companion", "CreatorCompanion.exe"),
                }
            )
                if (File.Exists(candidate))
                    return candidate;
            return null;
#elif UNITY_EDITOR_OSX
            foreach (var candidate in new[] { "/Applications/ALCOM.app", "/Applications/VCC.app" })
                if (Directory.Exists(candidate))
                    return candidate;
            return null;
#else
            return null;
#endif
        }

#if UNITY_EDITOR_WIN
        /// <summary>vcc: に紐付いているアプリの実行ファイルを、レジストリの起動コマンドから取り出す。
        /// 値は `"C:\...\ALCOM.exe" link "%1"` のような形なので、先頭の実行ファイルだけを見る。</summary>
        static string FromProtocolHandler()
        {
            try
            {
                foreach (var root in new[]
                {
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes\vcc\shell\open\command"),
                    Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"vcc\shell\open\command"),
                })
                {
                    if (root == null)
                        continue;
                    using (root)
                    {
                        if (!(root.GetValue(null) is string command) || string.IsNullOrEmpty(command))
                            continue;
                        var path = FirstArgument(command);
                        if (path != null && File.Exists(path))
                            return path;
                    }
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning("[ReFrameCore] vcc: の紐付けを読めませんでした: " + error.Message);
            }
            return null;
        }

        /// <summary>起動コマンドの文字列から実行ファイルの部分だけを取り出す。</summary>
        static string FirstArgument(string command)
        {
            command = command.Trim();
            if (command.StartsWith("\"", StringComparison.Ordinal))
            {
                var end = command.IndexOf('"', 1);
                return end > 1 ? command.Substring(1, end - 1) : null;
            }
            var space = command.IndexOf(' ');
            return space > 0 ? command.Substring(0, space) : command;
        }
#endif
    }
}
