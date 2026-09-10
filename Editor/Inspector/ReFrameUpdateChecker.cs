using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace jp.illusive_isc.ReFrame.Core.Editor
{
    /// <summary>ReFrame のパッケージ (Core とアバター用) の更新確認と、ダイアログを経た更新。</summary>
    internal static class ReFrameUpdateChecker
    {
        sealed class State
        {
            public string Name;
            public string DisplayName;
            public string Current;
            public string Latest;
            public string ZipUrl;
            public string Sha256;
            public string Error;
            public bool Checking;
            public DateTime CheckedAt;
        }

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

                // TODO: 動作確認用。更新が無くても押せるように常に出している。
                // 確認が済んだら消して、更新通知の行にあるボタンだけにする。
                if (ReFrameVccLauncher.IsAvailable)
                {
                    var always = new Button(ReFrameVccLauncher.Open) { text = "VCC を開く (仮)" };
                    always.tooltip = "VCC (または ALCOM) を開きます。動作確認用に常に表示しています。";
                    always.style.height = 24;
                    always.style.marginBottom = 6;
                    box.Add(always);
                }

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
                    var update = new Button(() => BeginUpdate(captured, state, Render)) { text = "更新する…" };
                    update.style.height = 24;
                    update.style.flexGrow = 1;
                    row.Add(update);
                    if (ReFrameVccLauncher.IsAvailable)
                    {
                        var openVcc = new Button(ReFrameVccLauncher.Open) { text = "VCC を開く" };
                        openVcc.tooltip =
                            "VCC (または ALCOM) を開きます。更新はそちらの Manage Packages で行ってください。";
                        openVcc.style.height = 24;
                        row.Add(openVcc);
                    }
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
                if (parts.Length >= 4 && long.TryParse(parts[3], out var ticks)
                    && DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) < CacheFor)
                {
                    state.Latest = parts[0];
                    state.ZipUrl = parts[1];
                    state.Sha256 = parts[2];
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
                    state.ZipUrl = (string)versions[best]?["url"];
                    state.Sha256 = (string)versions[best]?["zipSHA256"];
                    SessionState.SetString(
                        "ReFrame.Update.Cache." + package.name,
                        state.Latest + "\n" + state.ZipUrl + "\n" + state.Sha256 + "\n" + DateTime.UtcNow.Ticks
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

        static void BeginUpdate(PackageInfo package, State state, Action onDone)
        {
            var folder = package.resolvedPath;
            var packagesRoot = Path.GetFullPath("Packages");
            var insidePackages = Path.GetFullPath(folder).StartsWith(packagesRoot, StringComparison.OrdinalIgnoreCase);
            var isGitCopy = Directory.Exists(Path.Combine(folder, ".git"));
            if (!insidePackages || isGitCopy || string.IsNullOrEmpty(state.ZipUrl))
            {
                var message =
                    state.DisplayName + " " + state.Latest + " が公開されていますが、このプロジェクトのコピーは"
                    + (isGitCopy ? " git の作業コピー" : insidePackages ? " zip の配布が無い版" : " Packages の外にある")
                    + "ので、ここからは入れ替えません。"
                    + (char)10
                    + "VCC (または git) で更新してください。";
                if (ReFrameVccLauncher.IsAvailable && !isGitCopy)
                {
                    if (EditorUtility.DisplayDialog("ReFrame の更新", message, "VCC を開く", "OK"))
                        ReFrameVccLauncher.Open();
                }
                else
                {
                    EditorUtility.DisplayDialog("ReFrame の更新", message, "OK");
                }
                return;
            }

            var proceed = EditorUtility.DisplayDialog(
                "ReFrame の更新",
                state.DisplayName + " を " + state.Current + " から " + state.Latest + " に更新します。"
                    + (char)10
                    + (char)10
                    + "・zip をダウンロードして検証します"
                    + (char)10
                    + "・" + folder.Replace('\\', '/') + " の中身を入れ替えます"
                    + (char)10
                    + "・Packages/vpm-manifest.json に載っていればバージョンを書き換えます"
                    + (char)10
                    + "・その後 Unity が再読み込みします (数十秒)"
                    + (char)10
                    + (char)10
                    + "続けますか？",
                "更新する",
                "やめる"
            );
            if (!proceed)
                return;
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var temp = Path.Combine(Path.GetTempPath(), "ReFrameUpdate_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            var zipPath = Path.Combine(temp, "package.zip");
            var request = new UnityWebRequest(state.ZipUrl, UnityWebRequest.kHttpVerbGET)
            {
                downloadHandler = new DownloadHandlerFile(zipPath),
                timeout = 120,
            };
            EditorUtility.DisplayProgressBar("ReFrame の更新", state.DisplayName + " " + state.Latest + " をダウンロードしています", 0.1f);
            var operation = request.SendWebRequest();
            operation.completed += _ =>
            {
                try
                {
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        EditorUtility.ClearProgressBar();
                        EditorUtility.DisplayDialog("ReFrame の更新", "ダウンロードに失敗しました: " + request.error, "OK");
                        return;
                    }
                    EditorUtility.DisplayProgressBar("ReFrame の更新", "検証して入れ替えています", 0.6f);
                    var error = Install(zipPath, temp, folder, package.name, state);
                    EditorUtility.ClearProgressBar();
                    if (error != null)
                    {
                        EditorUtility.DisplayDialog("ReFrame の更新", "更新できませんでした: " + error, "OK");
                        return;
                    }
                    SessionState.EraseString("ReFrame.Update.Cache." + package.name);
                    States.Remove(package.name);
                    Debug.Log("[ReFrameCore] " + state.DisplayName + " を " + state.Current + " → " + state.Latest + " に更新しました。");
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    EditorUtility.DisplayDialog("ReFrame の更新", state.DisplayName + " を " + state.Latest + " に更新しました。", "OK");
                    onDone?.Invoke();
                }
                finally
                {
                    request.Dispose();
                    try { Directory.Delete(temp, true); } catch { }
                }
            };
        }

        /// <summary>zip を検証して展開し、フォルダを入れ替える。</summary>
        static string Install(string zipPath, string temp, string folder, string packageName, State state)
        {
            if (!string.IsNullOrEmpty(state.Sha256))
            {
                using (var stream = File.OpenRead(zipPath))
                using (var sha = SHA256.Create())
                {
                    var hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                    if (!string.Equals(hash, state.Sha256, StringComparison.OrdinalIgnoreCase))
                        return "zip の SHA256 が一覧と一致しません";
                }
            }
            var extracted = Path.Combine(temp, "extracted");
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extracted);

            var root = extracted;
            if (!File.Exists(Path.Combine(root, "package.json")))
            {
                var candidates = Directory.GetDirectories(extracted).Where(d => File.Exists(Path.Combine(d, "package.json"))).ToArray();
                if (candidates.Length != 1)
                    return "zip の中に package.json が見つかりません";
                root = candidates[0];
            }
            var manifest = JObject.Parse(File.ReadAllText(Path.Combine(root, "package.json")));
            if ((string)manifest["name"] != packageName)
                return "zip の package.json の name が違います (" + manifest["name"] + ")";
            var newVersion = (string)manifest["version"];

            var backup = Path.Combine(temp, "backup");
            Directory.Move(folder, backup);
            try
            {
                CopyDirectory(root, folder);
            }
            catch (Exception e)
            {
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); Directory.Move(backup, folder); } catch { }
                return "入れ替え中に失敗しました: " + e.Message;
            }

            var manifestPath = Path.Combine("Packages", "vpm-manifest.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    var vpm = JObject.Parse(File.ReadAllText(manifestPath));
                    var changed = false;
                    foreach (var section in new[] { "dependencies", "locked" })
                    {
                        var entry = vpm[section]?[packageName] as JObject;
                        if (entry != null && entry["version"] != null)
                        {
                            entry["version"] = newVersion;
                            changed = true;
                        }
                    }
                    if (changed)
                        File.WriteAllText(manifestPath, vpm.ToString(Newtonsoft.Json.Formatting.Indented));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[ReFrameCore] vpm-manifest.json を書き換えられませんでした: " + e.Message);
                }
            }
            return null;
        }

        static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(from))
                CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
        }
    }
}
