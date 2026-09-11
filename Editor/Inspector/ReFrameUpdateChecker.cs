using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>ReFrame のパッケージ (Core とアバター用) の更新確認。入れ替えは更新ページの unitypackage で行う。</summary>
    internal static class ReFrameUpdateChecker
    {
        sealed class State
        {
            public string Name;
            public string DisplayName;
            public string Current;
            public string Latest;
            public string Error;
            public bool Checking;
            public DateTime CheckedAt;
        }

        /// <summary>取り込むだけで最新版が入る unitypackage を配っているページ。</summary>
        const string InstallPageUrl = "https://reframe.illusive-isc.jp/install/";

        static void OpenInstallPage() => UnityEngine.Application.OpenURL(InstallPageUrl);

        static readonly Dictionary<string, State> States = new Dictionary<string, State>();
        static readonly TimeSpan CacheFor = TimeSpan.FromHours(1);

        /// <summary>Inspector の最上段に置く通知。</summary>
        internal static VisualElement BuildNotice(ReFrameDeleteComponent component)
        {
            var box = new VisualElement();
            var packages = new List<PackageInfo>();
            var core = PackageInfo.FindForAssembly(typeof(ReFrameDeleteComponent).Assembly);
            if (core != null)
                packages.Add(core);
            var avatar = PackageInfo.FindForAssembly(component.GetType().Assembly);
            if (avatar != null && (core == null || avatar.name != core.name))
                packages.Add(avatar);

            void Render()
            {
                box.Clear();

                foreach (var package in packages)
                {
                    if (!States.TryGetValue(package.name, out var state))
                        continue;
                    if (state.Checking || string.IsNullOrEmpty(state.Latest))
                        continue;
                    if (Compare(state.Latest, state.Current) <= 0)
                        continue;
                    var captured = package;
                    var notice = new HelpBox(
                        state.DisplayName + " の新しい版 " + state.Latest + " が公開されています (いまは " + state.Current + ")。",
                        HelpBoxMessageType.Info
                    );
                    notice.style.marginBottom = 2;
                    box.Add(notice);
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.marginBottom = 6;
                    var openPage = new Button(OpenInstallPage) { text = "更新ページを開く" };
                    openPage.tooltip =
                        "ブラウザで更新ページを開きます。ダウンロードしたファイルを Unity に取り込むと最新版に入れ替わります。";
                    openPage.style.height = 24;
                    openPage.style.flexGrow = 1;
                    row.Add(openPage);
                    var later = new Button(() =>
                    {

                        SessionState.SetString("ReFrame.Update.Dismissed." + captured.name, state.Latest);
                        Render();
                    })
                    { text = "後で" };
                    later.style.height = 24;
                    row.Add(later);
                    box.Add(row);
                    if (SessionState.GetString("ReFrame.Update.Dismissed." + captured.name, "") == state.Latest)
                    {
                        box.Remove(notice);
                        box.Remove(row);
                    }
                }
            }

            foreach (var package in packages)
                EnsureChecked(package, Render);
            Render();
            return box;
        }

        static void EnsureChecked(PackageInfo package, Action onDone)
        {
            if (States.TryGetValue(package.name, out var state))
            {
                if (state.Checking || DateTime.UtcNow - state.CheckedAt < CacheFor)
                    return;
            }
            state = new State
            {
                Name = package.name,
                DisplayName = string.IsNullOrEmpty(package.displayName) ? package.name : package.displayName,
                Current = package.version,
            };
            States[package.name] = state;

            var url = DistributionUrl(package);
            if (string.IsNullOrEmpty(url))
            {
                state.CheckedAt = DateTime.UtcNow;
                return;
            }

            var cached = SessionState.GetString("ReFrame.Update.Cache." + package.name, "");
            if (!string.IsNullOrEmpty(cached))
            {
                var parts = cached.Split('\n');
                if (parts.Length >= 2 && long.TryParse(parts[1], out var ticks)
                    && DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) < CacheFor)
                {
                    state.Latest = parts[0];
                    state.CheckedAt = new DateTime(ticks, DateTimeKind.Utc);
                    return;
                }
            }

            state.Checking = true;
            var request = UnityWebRequest.Get(url);
            request.timeout = 15;
            var operation = request.SendWebRequest();
            operation.completed += _ =>
            {
                try
                {
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        state.Error = request.error;
                        return;
                    }
                    var listing = JObject.Parse(request.downloadHandler.text);
                    var versions = listing["packages"]?[package.name]?["versions"] as JObject;
                    if (versions == null)
                    {
                        state.Error = "一覧にこのパッケージがありません";
                        return;
                    }
                    string best = null;
                    foreach (var property in versions.Properties())
                        if (best == null || Compare(property.Name, best) > 0)
                            best = property.Name;
                    if (best == null)
                        return;
                    state.Latest = best;
                    SessionState.SetString(
                        "ReFrame.Update.Cache." + package.name,
                        state.Latest + "\n" + DateTime.UtcNow.Ticks
                    );
                }
                catch (Exception e)
                {
                    state.Error = e.Message;
                }
                finally
                {
                    state.Checking = false;
                    state.CheckedAt = DateTime.UtcNow;
                    request.Dispose();
                    onDone?.Invoke();
                }
            };
        }

        /// <summary>package.json の vpmDistributionUrl。</summary>
        static string DistributionUrl(PackageInfo package)
        {
            try
            {
                var path = Path.Combine(package.resolvedPath, "package.json");
                if (!File.Exists(path))
                    return null;
                var json = JObject.Parse(File.ReadAllText(path));
                return (string)json["vpmDistributionUrl"] ?? (string)json["repo"];
            }
            catch
            {
                return null;
            }
        }

        /// <summary>"1.2.3" 形式の比較。</summary>
        internal static int Compare(string a, string b)
        {
            var x = Parse(a);
            var y = Parse(b);
            for (var i = 0; i < 3; i++)
                if (x[i] != y[i])
                    return x[i].CompareTo(y[i]);
            return 0;
        }

        static int[] Parse(string version)
        {
            var result = new int[3];
            if (string.IsNullOrEmpty(version))
                return result;
            var core = version.Split('-', '+')[0];
            var parts = core.Split('.');
            for (var i = 0; i < 3 && i < parts.Length; i++)
                int.TryParse(parts[i], out result[i]);
            return result;
        }
    }
}
