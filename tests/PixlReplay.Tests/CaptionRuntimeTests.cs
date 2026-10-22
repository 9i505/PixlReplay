using PixlReplay.Engine.Captions;
using PixlReplay.Ui.Edit;

namespace PixlReplay.Tests;

/// <summary>
/// Auto captions' engine (whisper, 14 MB) downloads on first use with the model, under one consent, into PixlReplay's local
/// folder; a damaged or cut download leaves nothing behind.
/// </summary>
public static class CaptionRuntimeTests
{
    /// <summary>The runtime zip the release script keeps in the repository's releases folder (git-ignored).</summary>
    static readonly string Zip = FindZip();

    static string FindZip()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "releases", "captions-runtime", CaptionRuntime.AssetName);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException("releases\\captions-runtime\\" + CaptionRuntime.AssetName + " not found (made from third_party\\whisper\\bin)");
    }

    static string Temp(string name) => Path.Combine(Path.GetTempPath(), "pixlreplay-tests", name + "-" + Guid.NewGuid().ToString("N"));

    static CaptionRuntime Runtime(string folder, string source) => new(folder, source);

    [Test]
    public static void The_runtime_downloads_checks_and_unpacks()
    {
        string root = Temp("runtime");
        try
        {
            var runtime = Runtime(Path.Combine(root, "runtime"), Path.GetFullPath(Zip));
            Assert.True(!runtime.IsPresent, "not yet");
            double last = 0;
            runtime.Download(consent: true, new InlineProgress(v => last = v), CancellationToken.None).GetAwaiter().GetResult();
            Assert.True(runtime.IsPresent, "unpacked");
            foreach (string dll in CaptionRuntime.Files)
                Assert.True(File.Exists(Path.Combine(root, "runtime", dll)), dll);
            Assert.True(last >= 0.99, $"progress to the end ({last})");
            Assert.True(Directory.GetFiles(root, "*.part").Length == 0, "no part file left");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A damaged or cut download keeps nothing (no half folder, no part file) and can be tried again.</summary>
    [Test]
    public static void A_wrong_or_cut_download_leaves_nothing()
    {
        string root = Temp("runtime-bad");
        Directory.CreateDirectory(root);
        try
        {
            byte[] bytes = File.ReadAllBytes(Zip);
            bytes[bytes.Length / 2] ^= 0xFF;
            string damaged = Path.Combine(root, "damaged.zip");
            File.WriteAllBytes(damaged, bytes);
            string cut = Path.Combine(root, "cut.zip");
            File.WriteAllBytes(cut, bytes[..(bytes.Length / 3)]);
            string folder = Path.Combine(root, "runtime");
            foreach (string source in new[] { damaged, cut })
            {
                bool failed = false;
                try
                {
                    Runtime(folder, source).Download(consent: true, null, CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (IOException)
                {
                    failed = true;
                }
                Assert.True(failed, $"{Path.GetFileName(source)}: refused");
                Assert.True(!Directory.Exists(folder) && Directory.GetFiles(root, "*.part").Length == 0 && Directory.GetDirectories(root).Length == 0,
                    $"{Path.GetFileName(source)}: nothing kept");
            }
            var good = Runtime(folder, Path.GetFullPath(Zip));
            good.Download(consent: true, null, CancellationToken.None).GetAwaiter().GetResult();
            Assert.True(good.IsPresent, "the real one works after");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public static void It_downloads_only_after_consent()
    {
        string root = Temp("runtime-ask");
        bool refused = false;
        try
        {
            Runtime(Path.Combine(root, "runtime"), Path.GetFullPath(Zip)).Download(consent: false, null, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException)
        {
            refused = true;
        }
        Assert.True(refused && !Directory.Exists(root), "nothing fetched without a yes");
    }

    /// <summary>The consent sheet names the size of what is missing: the engine, the model, or both.</summary>
    [Test]
    public static void The_question_counts_what_is_missing()
    {
        string root = Temp("captions");
        try
        {
            var model = new CaptionModel(Path.Combine(root, "model"));
            var runtime = Runtime(Path.Combine(root, "runtime"), Path.GetFullPath(Zip));
            var maker = new CaptionMaker(model, runtime, "PixlReplay.exe");
            Assert.True(!maker.Ready, "nothing yet");
            Assert.Equal(CaptionModel.Bytes + CaptionRuntime.Bytes, maker.DownloadBytes, "both");
            runtime.Download(consent: true, null, CancellationToken.None).GetAwaiter().GetResult();
            Assert.True(!maker.Ready, "the model is still missing");
            Assert.Equal(CaptionModel.Bytes, maker.DownloadBytes, "the model only");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A model already downloaded (in the old roaming folder) moves to PixlReplay's local folder instead of downloading again.</summary>
    [Test]
    public static void The_old_model_moves_instead_of_downloading()
    {
        string root = Temp("model-move");
        string old = Path.Combine(root, "old");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, CaptionModel.FileName), "model");
        try
        {
            var model = new CaptionModel(Path.Combine(root, "new"));
            Assert.True(model.MoveFrom(old), "moved");
            Assert.True(File.Exists(model.Path) && !File.Exists(Path.Combine(old, CaptionModel.FileName)), "now in the new folder only");
            File.WriteAllText(Path.Combine(old, CaptionModel.FileName), "another");
            Assert.True(!model.MoveFrom(old), "the new one is kept");
            Assert.True(File.ReadAllText(model.Path) == "model", "untouched");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
