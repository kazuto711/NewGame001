// Windows 用の実行ファイルを作るツール。出力先は NewGame/Builds/Windows/Matoi.exe。
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Matoi.EditorTools
{
    public static class MatoiBuild
    {
        const string Output = "Builds/Windows/Matoi.exe";

        [MenuItem("Matoi/Build Windows (体験版の作成)")]
        public static void BuildWindows()
        {
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[Matoi] build result={report.summary.result} size={report.summary.totalSize / (1024 * 1024)}MB errors={report.summary.totalErrors}");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
