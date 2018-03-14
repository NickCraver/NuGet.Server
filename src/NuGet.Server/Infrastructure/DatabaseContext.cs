using Microsoft.EntityFrameworkCore;
using System;

namespace NuGet.Server.Infrastructure
{
	public class DatabaseContext : DbContext
	{
		private readonly string _connectionString;
		private readonly DatabaseType _dbType;

		public enum DatabaseType
		{
			SqlServer
		}

		public DatabaseContext(string databaseType, string connetionString)
		{
			_connectionString = connetionString;
			_dbType = Enum.TryParse<DatabaseType>(databaseType, true, out var dbType) ? dbType : DatabaseType.SqlServer;
		}

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			// TODO: This could faily easily be platform agnostic, but let's get the DI story figured out first
			optionsBuilder.UseSqlServer(_connectionString);
		}

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			var tbl = modelBuilder.Entity<DatabasePackage>()
								  .ToTable("Packages")
								  .Ignore(p => p.PackageData)
								  .Ignore(p => p.AssemblyReferences)
								  .Ignore(p => p.Authors)
								  .Ignore(p => p.DependencySets)
								  .Ignore(p => p.FrameworkAssemblies)
								  .Ignore(p => p.FullPath)
								  .Ignore(p => p.IconUrl)
								  .Ignore(p => p.LicenseUrl)
								  .Ignore(p => p.MinClientVersion)
								  .Ignore(p => p.Owners)
								  .Ignore(p => p.PackageAssemblyReferences)
								  .Ignore(p => p.PackageHash)
								  .Ignore(p => p.PackageHashAlgorithm)
								  .Ignore(p => p.Path)
								  .Ignore(p => p.ProjectUrl)
								  .Ignore(p => p.ReportAbuseUrl)
								  .Ignore(p => p.Version);

			tbl.Property(p => p.Language).HasMaxLength(20);
			tbl.Property(p => p.MinClientVersionBacking).HasMaxLength(44);
			tbl.Property(p => p.Tags).HasMaxLength(2000);
			tbl.Property(p => p.Title).HasMaxLength(256);
			tbl.Property(p => p.VersionBacking).HasMaxLength(64).IsRequired();

			

			var data = modelBuilder.Entity<DatabasePackageData>()
				.ToTable("PackagesData")
				.Ignore(p => p.Package);

			data.HasKey("PackageId", "Version");

			tbl.HasOne(dp => dp.PackageData)
			.WithOne(d => d.Package);
		}

		public DbSet<DatabasePackage> Packages { get; set; }
		public DbSet<DatabasePackageData> PackagesData { get; set; }
	}
}