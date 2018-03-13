// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Web.Configuration;
using NuGet.Server.Logging;

namespace NuGet.Server.Infrastructure
{
    /// <summary>
    /// ServerPackageRepository represents a folder of nupkgs on disk. All packages are cached during the first request in order
    /// to correctly determine attributes such as IsAbsoluteLatestVersion. Adding, removing, or making changes to packages on disk 
    /// will clear the cache.
    /// </summary>
    public class DatabasePackageRepository
        : PackageRepositoryBase, IServerPackageRepository, IPackageLookup, IDisposable
    {
        private readonly Logging.ILogger _logger;
        private readonly Func<string, bool, bool> _getSetting;

        // TODO: Repace DI so this can be per-request
        private DatabaseContext DB { get; }

        public DatabasePackageRepository(IHashProvider hashProvider, Logging.ILogger logger)
        {
            _ = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
            _logger = logger ?? new TraceLogger();
            _getSetting = GetBooleanAppSetting;

            DB = new DatabaseContext(DatabaseType, ConnectionString);
            DB.Database.EnsureCreated();
        }

        public override IQueryable<IPackage> GetPackages() => DB.Packages;

        public IQueryable<ServerPackage> GetPackages(ClientCompatibility compatibility)
        {
            return compatibility.AllowSemVer2
                 ? DB.Packages
				 : DB.Packages.Where(p => !p.IsSemVer2);
        }

        public bool Exists(string packageId, SemanticVersion version)
        {
            return DB.Packages.Any(p => p.Id == packageId
                                       && p.VersionBacking == DatabasePackage.GetVersionString(version));
        }

        public IPackage FindPackage(string packageId, SemanticVersion version)
        {
            // TODO: Account for SemVer1 vs 2 here
            return DB.Packages.FirstOrDefault(p => p.Id == packageId
                                                && p.VersionBacking == DatabasePackage.GetVersionString(version));
        }

        public IEnumerable<ServerPackage> FindPackagesById(string packageId, ClientCompatibility compatibility)
        {
            return DB.Packages.Where(p => p.Id == packageId
                                       && (compatibility.AllowSemVer2 || !p.IsSemVer2));
        }

        public IEnumerable<IPackage> FindPackagesById(string packageId) =>
            FindPackagesById(packageId, ClientCompatibility.Default);

        public IQueryable<IPackage> Search(
            string searchTerm,
            IEnumerable<string> targetFrameworks,
            bool allowPrereleaseVersions)
        {
            return Search(searchTerm, targetFrameworks, allowPrereleaseVersions, ClientCompatibility.Default);
        }

        public IQueryable<IPackage> Search(
            string searchTerm,
            IEnumerable<string> targetFrameworks,
            bool allowPrereleaseVersions,
            ClientCompatibility compatibility)
        {
            //terribad search ENGAGE
            var packages = DB.Packages
				.Where(p => p.Id.Contains(searchTerm)
                         || p.Description.Contains(searchTerm)
                         || p.Summary.Contains(searchTerm)
                         || p.Tags.Contains(searchTerm));
            if (!allowPrereleaseVersions)
            {
                packages = packages.Where(p => !p.IsPrerelase);
            }

            if (EnableDelisting)
            {
                packages = packages.Where(p => p.Listed);
            }

            if (EnableFrameworkFiltering && targetFrameworks.Any())
            {
                // Get the list of framework names
                var frameworkNames = targetFrameworks
                    .Select(VersionUtility.ParseFrameworkName);

                packages = packages
                    .Where(package => frameworkNames
                        .Any(frameworkName => VersionUtility
                            .IsCompatible(frameworkName, package.GetSupportedFrameworks())));
            }

            return packages.AsQueryable();
        }

        public IEnumerable<IPackage> GetUpdates(
            IEnumerable<IPackageName> packages,
            bool includePrerelease,
            bool includeAllVersions,
            IEnumerable<FrameworkName> targetFrameworks,
            IEnumerable<IVersionSpec> versionConstraints)
        {
            return this.GetUpdatesCore(
                packages,
                includePrerelease,
                includeAllVersions,
                targetFrameworks,
                versionConstraints,
                ClientCompatibility.Default);
        }

        public override string Source => "Database Server";
        public override bool SupportsPrereleasePackages => true;

        /// <summary>
        /// Add a file to the repository.
        /// </summary>
        /// <param name="package">The package to add to the database.</param>
        public override void AddPackage(IPackage package)
        {
            _logger.Log(LogLevel.Info, "Start adding package {0} {1}.", package.Id, package.Version);

            if (IgnoreSymbolsPackages && package.IsSymbolsPackage())
            {
                var message = string.Format(Strings.Error_SymbolsPackagesIgnored, package);

                _logger.Log(LogLevel.Error, message);
                throw new InvalidOperationException(message);
            }

            if (!AllowOverrideExistingPackageOnPush && Exists(package.Id, package.Version))
            {
                var message = string.Format(Strings.Error_PackageAlreadyExists, package);

                _logger.Log(LogLevel.Error, message);
                throw new InvalidOperationException(message);
            }

            DB.Packages.Add(new DatabasePackage(package));
			DB.PackagesData.Add(new DatabasePackageData(package));
            DB.SaveChanges();
            UpdateLatestVersions(package.Id);
            _logger.Log(LogLevel.Info, "Finished adding package {0} {1}.", package.Id, package.Version);
        }

        /// <summary>
        /// Unlist or delete a package.
        /// </summary>
        /// <param name="package">The package to remove from the database.</param>
        public override void RemovePackage(IPackage package)
        {
            if (package == null)
            {
                return;
            }

            _logger.Log(LogLevel.Info, "Start removing package {0} {1}.", package.Id, package.Version);

            var dbPackage = package as DatabasePackage;

            if (EnableDelisting)
            {
                dbPackage.Listed = false;
                _logger.Log(LogLevel.Info, "Unlisted package {0} {1}.", package.Id, package.Version);
            }
            else
            {
                DB.Remove(dbPackage);
                _logger.Log(LogLevel.Info, "Finished removing package {0} {1}.", package.Id, package.Version);
            }
            DB.SaveChanges();
            UpdateLatestVersions(package.Id);
        }

        private void UpdateLatestVersions(string packageId)
        {
            _logger.Log(LogLevel.Info, "Updating latest packages for {0}.", packageId);
            var all = FindPackagesById(packageId, ClientCompatibility.Max);
            // TODO: Move this somewhere more reusable
            ServerPackageStore.UpdateLatestVersions(all);
            DB.SaveChanges();
            _logger.Log(LogLevel.Info, "Finished updating latest packages for {0}.", packageId);
        }

        /// <summary>
        /// Remove a package from the respository.
        /// </summary>
        /// <param name="packageId">The Id of the package to remove.</param>
        /// <param name="version">The version of the package to remove.</param>
        public void RemovePackage(string packageId, SemanticVersion version)
        {
            var package = FindPackage(packageId, version);
            RemovePackage(package);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) { }

        //private DatabasePackage CreateDatabasePackage(IPackage package)
        //{
        //    // File names
        //    var packageFileName = GetPackageFileName(package.Id, package.Version);
        //    var hashFileName = GetHashFileName(package.Id, package.Version);

        //    return new DatabasePackage(package);
        //}

        /// <summary>
        /// Sets the current cache to null so it will be regenerated next time.
        /// </summary>
        public void ClearCache()
        {
            OptimizedZipPackage.PurgeCache();
            _logger.Log(LogLevel.Info, "Cleared package cache.");
        }

        private bool AllowOverrideExistingPackageOnPush => _getSetting("allowOverrideExistingPackageOnPush", true);
        private bool IgnoreSymbolsPackages => _getSetting("ignoreSymbolsPackages", false);
        private bool EnableDelisting => _getSetting("enableDelisting", false);
        private bool EnableFrameworkFiltering => _getSetting("enableFrameworkFiltering", false);
        private string ConnectionString => GetAppSetting("dbConnectionString");
        private string DatabaseType => GetAppSetting("dbType");

        private static string GetAppSetting(string key) => WebConfigurationManager.AppSettings[key];

        private static bool GetBooleanAppSetting(string key, bool defaultValue) =>
            !bool.TryParse(GetAppSetting(key), out bool value) ? defaultValue : value;

        //private const string TemplateNupkgFilename = "{0}\\{1}\\{0}.{1}.nupkg";
        //private string GetPackageFileName(string packageId, SemanticVersion version) =>
        //    string.Format(TemplateNupkgFilename, packageId, version.ToNormalizedString());
    }
}