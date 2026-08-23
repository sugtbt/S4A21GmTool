using System;
using System.Collections.Generic;
using System.Threading;
using DfoGmTool.ImagePack;
using DfoGmTool.ServerCore.GameWorld;
using DfoGmTool.ServerCore.Infrastructure;
using GmPvfLib;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    // Owns the currently selected data source so all GM endpoints switch together.
    public sealed class GmRuntimeEnvironment
    {
        private readonly ReaderWriterLockSlim _gate = new ReaderWriterLockSlim();
        private ActiveEnvironment _active;
        private string _startupError;

        public GmRuntimeEnvironment(GmConfig initialConfig, string imagePacksPath = null)
        {
            if (initialConfig != null)
                Configure(initialConfig, imagePacksPath);
        }

        public RuntimeEnvironmentStatus GetStatus(bool includeSourceDetails = true)
        {
            _gate.EnterReadLock();
            try
            {
                return BuildStatus(includeSourceDetails);
            }
            finally
            {
                _gate.ExitReadLock();
            }
        }

        public object Configure(string databasePath, string pvfPath, string imagePacksPath = null)
        {
            if (!GmConfig.TryCreate(databasePath, pvfPath, out var config, out var error))
                return Failure(error);

            return Configure(config, imagePacksPath);
        }

        public object Execute(Func<GmService, PvfIndexService, object> operation)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            _gate.EnterReadLock();
            try
            {
                if (_active == null)
                    return Failure("请先选择数据库和 PVF。" );
                if (!string.IsNullOrWhiteSpace(_active.PvfIndex.BuildError))
                    return Failure("PVF 加载失败: " + _active.PvfIndex.BuildError);
                if (!_active.PvfIndex.IsReady)
                    return Failure("PVF 正在加载，请稍候。" );

                return operation(_active.Gm, _active.PvfIndex);
            }
            finally
            {
                _gate.ExitReadLock();
            }
        }

        public ItemIconResult TryGetItemIcon(int itemId)
        {
            _gate.EnterReadLock();
            try
            {
                if (_active == null)
                    return ItemIconResult.Fail("请先选择数据库和 PVF。");
                if (!string.IsNullOrWhiteSpace(_active.PvfIndex.BuildError))
                    return ItemIconResult.Fail("PVF 加载失败: " + _active.PvfIndex.BuildError);
                if (!_active.PvfIndex.IsReady)
                    return ItemIconResult.Fail("PVF 正在加载，请稍候。");
                if (!_active.PvfIndex.TryGetIcon(itemId, out var iconPath, out var iconFrame, out var markPath, out var markFrame))
                    return ItemIconResult.Missing();
                if (_active.ImagePacks == null
                    || !_active.ImagePacks.TryRenderPng(iconPath, iconFrame, markPath, markFrame, out var png))
                    return ItemIconResult.Missing();
                return ItemIconResult.Ok(png);
            }
            finally
            {
                _gate.ExitReadLock();
            }
        }

        public ItemIconResult TryGetWindowChrome()
        {
            _gate.EnterReadLock();
            try
            {
                if (_active?.ImagePacks == null)
                    return ItemIconResult.Missing();
                if (!_active.ImagePacks.TryRenderWindowChrome(out var png))
                    return ItemIconResult.Missing();
                return ItemIconResult.Ok(png);
            }
            finally
            {
                _gate.ExitReadLock();
            }
        }

        private object Configure(GmConfig config, string imagePacksPath)
        {
            _gate.EnterWriteLock();
            try
            {
                try
                {
                    var requestedImagePacks = string.IsNullOrWhiteSpace(imagePacksPath) ? null : imagePacksPath.Trim();
                    var imagePacks = ImagePackLibrary.TryOpen(requestedImagePacks);
                    var resolvedImagePacks = imagePacks != null ? imagePacks.Root : requestedImagePacks;

                    if (_active != null
                        && PathsEqual(_active.Config.DatabasePath, config.DatabasePath)
                        && PathsEqual(_active.Config.PvfPath, config.PvfPath))
                    {
                        var imagePacksChanged = !PathsEqual(_active.ImagePacksPath, resolvedImagePacks);
                        if (imagePacksChanged)
                        {
                            _active.ReplaceImagePacks(imagePacks, resolvedImagePacks);
                            LogImagePacks(imagePacks, requestedImagePacks);
                        }

                        return new
                        {
                            success = true,
                            sourceChanged = false,
                            imagePacksChanged,
                            status = BuildStatus(),
                        };
                    }

                    VerifyDataSource(config);

                    // Construct the new services before replacing the live source.
                    var pvfIndex = new PvfIndexService(config.PvfPath);
                    var gm = new GmService(config, pvfIndex);
                    LogImagePacks(imagePacks, requestedImagePacks);

                    Environment.SetEnvironmentVariable("PVF_ARCHIVE_PATH", config.PvfPath);
                    Environment.SetEnvironmentVariable("INVENTORY_DATABASE_PATH", config.DatabasePath);
                    PvfArchiveAccessor.Configure(config.PvfPath);
                    PvfRuntimeCache.ResetForPvfChange();
                    GmService.ResetPvfStaticData();

                    _active = new ActiveEnvironment(config, gm, pvfIndex, imagePacks, resolvedImagePacks);
                    _startupError = null;
                    pvfIndex.WarmInBackground();
                    return new
                    {
                        success = true,
                        sourceChanged = true,
                        imagePacksChanged = true,
                        status = BuildStatus(),
                    };
                }
                catch (Exception ex)
                {
                    var error = ex.GetBaseException().Message;
                    if (_active == null)
                        _startupError = error;
                    return Failure(error);
                }
            }
            finally
            {
                _gate.ExitWriteLock();
            }
        }

        private static void LogImagePacks(ImagePackLibrary imagePacks, string requestedPath)
        {
            if (imagePacks != null)
            {
                Console.WriteLine("[ImagePack] 图标目录: " + imagePacks.Root);
                return;
            }

            Console.WriteLine(string.IsNullOrWhiteSpace(requestedPath)
                ? "[ImagePack] 未选择 ImagePacks2，物品预览只有文字没有图标"
                : "[ImagePack] ImagePacks2 目录无效，物品预览只有文字没有图标");
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
                return true;
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;
            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static void VerifyDataSource(GmConfig config)
        {
            var errors = new List<string>();
            AddVerificationError(errors, "数据库", () => VerifyDatabase(config));
            AddVerificationError(errors, "PVF", () => VerifyPvf(config));
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        private static void AddVerificationError(List<string> errors, string label, Action verify)
        {
            try
            {
                verify();
            }
            catch (Exception ex)
            {
                errors.Add(label + "校验失败: " + ex.GetBaseException().Message);
            }
        }

        private static void VerifyDatabase(GmConfig config)
        {
            using (var connection = new SqliteConnection(config.ConnectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT 1 FROM sqlite_master LIMIT 1;";
                    command.ExecuteScalar();
                }

                A21DatabaseGuard.EnsureA21Baseline(connection);
            }

            // 基线通过后再补灵魂列，避免 ListItems 查 accounts.soul_* 时报 no such column
            SqliteDatabaseBootstrap.Initialize(config.DatabasePath, config.SchemaPath);
        }

        private static void VerifyPvf(GmConfig config)
        {
            using (var archive = PvfArchive.Open(config.PvfPath))
            {
                if (string.IsNullOrWhiteSpace(archive.GetFileContent("stackable/stackable.lst")))
                    throw new InvalidOperationException("所选 PVF 缺少 stackable/stackable.lst。");
            }
        }

        private RuntimeEnvironmentStatus BuildStatus(bool includeSourceDetails = true)
        {
            var config = _active?.Config;
            var index = _active?.PvfIndex;
            var indexError = index?.BuildError;
            var ready = index != null && index.IsReady && string.IsNullOrWhiteSpace(indexError);
            return new RuntimeEnvironmentStatus
            {
                Configured = config != null,
                Ready = ready,
                Loading = config != null && !ready && string.IsNullOrWhiteSpace(indexError),
                Database = includeSourceDetails ? config?.DatabasePath : null,
                Pvf = includeSourceDetails ? config?.PvfPath : null,
                ImagePacks = includeSourceDetails ? _active?.ImagePacksPath : null,
                HasImagePacks = _active?.ImagePacks != null,
                ServerBin = includeSourceDetails ? config?.ServerBinDir : null,
                IndexReady = index?.IsReady ?? false,
                IndexError = includeSourceDetails ? indexError : null,
                Error = includeSourceDetails ? (config == null ? _startupError : indexError) : null,
                HasError = !string.IsNullOrWhiteSpace(config == null ? _startupError : indexError),
            };
        }

        private static object Failure(string error)
        {
            return new { success = false, error = error ?? "数据源加载失败。" };
        }

        private sealed class ActiveEnvironment
        {
            public ActiveEnvironment(
                GmConfig config,
                GmService gm,
                PvfIndexService pvfIndex,
                ImagePackLibrary imagePacks,
                string imagePacksPath)
            {
                Config = config;
                Gm = gm;
                PvfIndex = pvfIndex;
                ImagePacks = imagePacks;
                ImagePacksPath = imagePacksPath;
            }

            public GmConfig Config { get; }
            public GmService Gm { get; }
            public PvfIndexService PvfIndex { get; }
            public ImagePackLibrary ImagePacks { get; private set; }
            public string ImagePacksPath { get; private set; }

            public void ReplaceImagePacks(ImagePackLibrary imagePacks, string imagePacksPath)
            {
                ImagePacks = imagePacks;
                ImagePacksPath = imagePacksPath;
            }
        }
    }

    public readonly struct ItemIconResult
    {
        private ItemIconResult(byte[] png, string error)
        {
            Png = png;
            Error = error;
        }

        public byte[] Png { get; }
        public string Error { get; }

        public static ItemIconResult Ok(byte[] png) => new ItemIconResult(png, null);
        public static ItemIconResult Missing() => new ItemIconResult(null, null);
        public static ItemIconResult Fail(string error) => new ItemIconResult(null, error);
    }

    public sealed class RuntimeEnvironmentStatus
    {
        public bool Configured { get; set; }
        public bool Ready { get; set; }
        public bool Loading { get; set; }
        public string Database { get; set; }
        public string Pvf { get; set; }
        public string ImagePacks { get; set; }
        public bool HasImagePacks { get; set; }
        public string ServerBin { get; set; }
        public bool IndexReady { get; set; }
        public string IndexError { get; set; }
        public string Error { get; set; }
        public bool HasError { get; set; }
    }
}
