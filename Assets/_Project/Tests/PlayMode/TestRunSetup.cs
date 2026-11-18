// エディタの Test Runner から実行した時も、タイトル画面を出さず、本番の記録に触らないようにする。
using System.IO;
using Matoi.Missions;
using Matoi.SaveSystem;
using NUnit.Framework;
using UnityEngine;

namespace Matoi.Tests
{
    [SetUpFixture]
    public class TestRunSetup
    {
        string folder;

        [OneTimeSetUp]
        public void Begin()
        {
            GameFlow.ForceAutomated = true;
            folder = Path.Combine(Application.temporaryCachePath, "MatoiTestRun");
            Directory.CreateDirectory(folder);
            SaveManager.DirectoryOverride = folder;
        }

        [OneTimeTearDown]
        public void End()
        {
            GameFlow.ForceAutomated = false;
            SaveManager.DirectoryOverride = null;
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
