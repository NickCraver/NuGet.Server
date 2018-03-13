using Newtonsoft.Json;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;

namespace NuGet.Server.Infrastructure
{
	public class DatabasePackageData
	{
		[Column("PackageId")]
		[JsonRequired]
		public string PackageId { get; set; }
		[Column("Version")]
		public string Version { get; set; }
		[JsonIgnore]
		public byte[] PackageData { get; set; }
		public DateTimeOffset LastUpdated { get; set; }
		public DateTimeOffset Created { get; set; }

		public DatabasePackageData() { }

		public DatabasePackageData(IPackage package)
		{
			PackageId = package.Id;
			Version = package.Version.ToFullString();
			Created = DateTime.UtcNow;
			LastUpdated = DateTime.UtcNow;

			using (var ps = package.GetStream())
			using (var ms = new MemoryStream())
			{
				ps.CopyTo(ms);
				PackageData = ms.ToArray();
			}
		}
	}
}