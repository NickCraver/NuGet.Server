// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using Newtonsoft.Json;

namespace NuGet.Server.Infrastructure
{
    public class DatabasePackage : ServerPackage
    {
        public DatabasePackage() { }

        public DatabasePackage(IPackage package) : base(package, new PackageDerivedData())
        {
            Created = DateTime.UtcNow;
            LastUpdated = DateTime.UtcNow;

            using (var ps = package.GetStream())
            using (var ms = new MemoryStream())
            {
                ps.CopyTo(ms);
                PackageData = ms.ToArray();
                PackageSize = PackageData.Length;
            }
        }

        public static string GetVersionString(SemanticVersion version) => version.ToFullString();
        public static SemanticVersion GetSemanticVersion(string version) => SemanticVersion.TryParse(version, out var val) ? val : null;

        [JsonIgnore]
        public byte[] PackageData { get; set; }
        public override Stream GetStream() => new MemoryStream(PackageData);

        private bool? _isPrerelease;
        [JsonIgnore]
        public bool IsPrerelase
        {
            get => !string.IsNullOrEmpty(Version.SpecialVersion);
            // set ignored, we just want this to land in the database to be queryable
            set => _isPrerelease = value;
        }

        [JsonIgnore]
        [Column(nameof(Authors))]
        public string AuthorsBacking
        {
            get => string.Join(",", Authors);
            set => Authors = value?.Split(',');
        }

        [JsonIgnore]
        [Column(nameof(Owners))]
        public string OwnersBacking
        {
            get => string.Join(",", Owners);
            set => Owners = value?.Split(',');
        }

        [JsonIgnore]
        [Column(nameof(Version))]
        public string VersionBacking
        {
            get => GetVersionString(Version);
            set => Version = GetSemanticVersion(value);
        }

        [JsonIgnore]
        [Column(nameof(IconUrl))]
        internal string IconUrlBacking
        {
            get => IconUrl?.ToString();
            set => IconUrl = new Uri(value);
        }

        [JsonIgnore]
        [Column(nameof(LicenseUrl))]
        internal string LicenseUrlBacking
        {
            get => LicenseUrl?.ToString();
            set => LicenseUrl = new Uri(value);
        }

        [JsonIgnore]
        [Column(nameof(ProjectUrl))]
        internal string ProjectUrlBacking
        {
            get => ProjectUrl?.ToString();
            set => ProjectUrl = new Uri(value);
        }

        [JsonIgnore]
        [Column(nameof(MinClientVersion))]
        public string MinClientVersionBacking
        {
            get => MinClientVersion?.ToString();
            set => MinClientVersion = System.Version.TryParse(value, out var val) ? val : null;
        }

        [JsonIgnore]
        [Column(nameof(ReportAbuseUrl))]
        internal string ReportAbuseUrlBacking
        {
            get => ReportAbuseUrl?.ToString();
            set => ReportAbuseUrl = new Uri(value);
        }
    }
}
