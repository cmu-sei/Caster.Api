﻿// <auto-generated />
using System;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Caster.Api.Data.Migrations
{
    [DbContext(typeof(CasterContext))]
    [Migration("20220324033700_partitionPoolId")]
    partial class partitionPoolId
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "6.0.1")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.HasPostgresExtension(modelBuilder, "uuid-ossp");
            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Caster.Api.Domain.Models.Apply", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Output")
                        .HasColumnType("text")
                        .HasColumnName("output");

                    b.Property<Guid>("RunId")
                        .HasColumnType("uuid")
                        .HasColumnName("run_id");

                    b.Property<int>("Status")
                        .HasColumnType("integer")
                        .HasColumnName("status");

                    b.HasKey("Id");

                    b.HasIndex("RunId")
                        .IsUnique();

                    b.ToTable("applies");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Directory", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<Guid?>("ParentId")
                        .HasColumnType("uuid")
                        .HasColumnName("parent_id");

                    b.Property<string>("Path")
                        .HasColumnType("text")
                        .HasColumnName("path");

                    b.Property<Guid>("ProjectId")
                        .HasColumnType("uuid")
                        .HasColumnName("project_id");

                    b.Property<string>("TerraformVersion")
                        .HasColumnType("text")
                        .HasColumnName("terraform_version");

                    b.HasKey("Id");

                    b.HasIndex("ParentId");

                    b.HasIndex("Path");

                    b.HasIndex("ProjectId");

                    b.ToTable("directories");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.File", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<bool>("AdministrativelyLocked")
                        .HasColumnType("boolean")
                        .HasColumnName("administratively_locked");

                    b.Property<string>("Content")
                        .HasColumnType("text")
                        .HasColumnName("content");

                    b.Property<DateTime?>("DateSaved")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("date_saved");

                    b.Property<Guid>("DirectoryId")
                        .HasColumnType("uuid")
                        .HasColumnName("directory_id");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("boolean")
                        .HasColumnName("is_deleted");

                    b.Property<Guid?>("LockedById")
                        .HasColumnType("uuid")
                        .HasColumnName("locked_by_id");

                    b.Property<Guid?>("ModifiedById")
                        .HasColumnType("uuid")
                        .HasColumnName("modified_by_id");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<Guid?>("WorkspaceId")
                        .HasColumnType("uuid")
                        .HasColumnName("workspace_id");

                    b.HasKey("Id");

                    b.HasIndex("DirectoryId");

                    b.HasIndex("LockedById");

                    b.HasIndex("ModifiedById");

                    b.HasIndex("WorkspaceId");

                    b.ToTable("files");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.FileVersion", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Content")
                        .HasColumnType("text")
                        .HasColumnName("content");

                    b.Property<DateTime?>("DateSaved")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("date_saved");

                    b.Property<DateTime?>("DateTagged")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("date_tagged");

                    b.Property<Guid>("FileId")
                        .HasColumnType("uuid")
                        .HasColumnName("file_id");

                    b.Property<Guid?>("ModifiedById")
                        .HasColumnType("uuid")
                        .HasColumnName("modified_by_id");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<string>("Tag")
                        .HasColumnType("text")
                        .HasColumnName("tag");

                    b.Property<Guid?>("TaggedById")
                        .HasColumnType("uuid")
                        .HasColumnName("tagged_by_id");

                    b.HasKey("Id");

                    b.HasIndex("FileId");

                    b.HasIndex("ModifiedById");

                    b.HasIndex("TaggedById");

                    b.ToTable("file_versions");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Host", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Datastore")
                        .HasColumnType("text")
                        .HasColumnName("datastore");

                    b.Property<bool>("Development")
                        .HasColumnType("boolean")
                        .HasColumnName("development");

                    b.Property<bool>("Enabled")
                        .HasColumnType("boolean")
                        .HasColumnName("enabled");

                    b.Property<int>("MaximumMachines")
                        .HasColumnType("integer")
                        .HasColumnName("maximum_machines");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<Guid?>("ProjectId")
                        .HasColumnType("uuid")
                        .HasColumnName("project_id");

                    b.HasKey("Id");

                    b.HasIndex("ProjectId");

                    b.ToTable("hosts");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.HostMachine", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid>("HostId")
                        .HasColumnType("uuid")
                        .HasColumnName("host_id");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<Guid>("WorkspaceId")
                        .HasColumnType("uuid")
                        .HasColumnName("workspace_id");

                    b.HasKey("Id");

                    b.HasIndex("HostId");

                    b.HasIndex("WorkspaceId");

                    b.ToTable("host_machines");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Module", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<DateTime?>("DateModified")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("date_modified");

                    b.Property<string>("Description")
                        .HasColumnType("text")
                        .HasColumnName("description");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<string>("Path")
                        .HasColumnType("text")
                        .HasColumnName("path");

                    b.HasKey("Id");

                    b.ToTable("modules");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.ModuleVersion", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<DateTime>("DateCreated")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("date_created");

                    b.Property<Guid>("ModuleId")
                        .HasColumnType("uuid")
                        .HasColumnName("module_id");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<string>("Outputs")
                        .HasColumnType("text")
                        .HasColumnName("outputs");

                    b.Property<string>("UrlLink")
                        .HasColumnType("text")
                        .HasColumnName("url_link");

                    b.Property<string>("Variables")
                        .HasColumnType("text")
                        .HasColumnName("variables");

                    b.HasKey("Id");

                    b.HasIndex("ModuleId");

                    b.ToTable("module_versions");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Partition", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<Guid>("PoolId")
                        .HasColumnType("uuid")
                        .HasColumnName("pool_id");

                    b.HasKey("Id");

                    b.HasIndex("PoolId");

                    b.ToTable("partitions");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Permission", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Description")
                        .HasColumnType("text")
                        .HasColumnName("description");

                    b.Property<string>("Key")
                        .HasColumnType("text")
                        .HasColumnName("key");

                    b.Property<bool>("ReadOnly")
                        .HasColumnType("boolean")
                        .HasColumnName("read_only");

                    b.Property<string>("Value")
                        .HasColumnType("text")
                        .HasColumnName("value");

                    b.HasKey("Id");

                    b.HasIndex("Key", "Value")
                        .IsUnique();

                    b.ToTable("permissions");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Plan", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Output")
                        .HasColumnType("text")
                        .HasColumnName("output");

                    b.Property<Guid>("RunId")
                        .HasColumnType("uuid")
                        .HasColumnName("run_id");

                    b.Property<int>("Status")
                        .HasColumnType("integer")
                        .HasColumnName("status");

                    b.HasKey("Id");

                    b.HasIndex("RunId")
                        .IsUnique();

                    b.ToTable("plans");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Pool", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.HasKey("Id");

                    b.ToTable("pools");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Project", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.HasKey("Id");

                    b.ToTable("projects");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.RemovedResource", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.HasKey("Id");

                    b.ToTable("removed_resources");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Run", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<Guid?>("CreatedById")
                        .HasColumnType("uuid")
                        .HasColumnName("created_by_id");

                    b.Property<bool>("IsDestroy")
                        .HasColumnType("boolean")
                        .HasColumnName("is_destroy");

                    b.Property<DateTime?>("ModifiedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("modified_at");

                    b.Property<Guid?>("ModifiedById")
                        .HasColumnType("uuid")
                        .HasColumnName("modified_by_id");

                    b.Property<int>("Status")
                        .HasColumnType("integer")
                        .HasColumnName("status");

                    b.Property<string>("Targets")
                        .HasColumnType("text")
                        .HasColumnName("targets");

                    b.Property<Guid>("WorkspaceId")
                        .HasColumnType("uuid")
                        .HasColumnName("workspace_id");

                    b.HasKey("Id");

                    b.HasIndex("CreatedAt");

                    b.HasIndex("CreatedById");

                    b.HasIndex("ModifiedById");

                    b.HasIndex("WorkspaceId");

                    b.ToTable("runs");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.User", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.HasKey("Id");

                    b.ToTable("users");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.UserPermission", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid>("PermissionId")
                        .HasColumnType("uuid")
                        .HasColumnName("permission_id");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid")
                        .HasColumnName("user_id");

                    b.HasKey("Id");

                    b.HasIndex("PermissionId");

                    b.HasIndex("UserId", "PermissionId")
                        .IsUnique();

                    b.ToTable("user_permissions");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Vlan", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<bool>("InUse")
                        .HasColumnType("boolean")
                        .HasColumnName("in_use");

                    b.Property<Guid>("PartitionId")
                        .HasColumnType("uuid")
                        .HasColumnName("partition_id");

                    b.Property<Guid>("PoolId")
                        .HasColumnType("uuid")
                        .HasColumnName("pool_id");

                    b.Property<int>("vlan")
                        .HasColumnType("integer")
                        .HasColumnName("vlan");

                    b.HasKey("Id");

                    b.HasIndex("PartitionId");

                    b.ToTable("vlans");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Workspace", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id")
                        .HasDefaultValueSql("uuid_generate_v4()");

                    b.Property<Guid>("DirectoryId")
                        .HasColumnType("uuid")
                        .HasColumnName("directory_id");

                    b.Property<bool>("DynamicHost")
                        .HasColumnType("boolean")
                        .HasColumnName("dynamic_host");

                    b.Property<Guid?>("HostId")
                        .HasColumnType("uuid")
                        .HasColumnName("host_id");

                    b.Property<DateTime?>("LastSynced")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("last_synced");

                    b.Property<string>("Name")
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<string>("State")
                        .HasColumnType("text")
                        .HasColumnName("state");

                    b.Property<string>("StateBackup")
                        .HasColumnType("text")
                        .HasColumnName("state_backup");

                    b.Property<string>("SyncErrors")
                        .HasColumnType("text")
                        .HasColumnName("sync_errors");

                    b.Property<string>("TerraformVersion")
                        .HasColumnType("text")
                        .HasColumnName("terraform_version");

                    b.HasKey("Id");

                    b.HasIndex("DirectoryId");

                    b.HasIndex("HostId");

                    b.ToTable("workspaces");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Apply", b =>
                {
                    b.HasOne("Caster.Api.Domain.Models.Run", "Run")
                        .WithOne("Apply")
                        .HasForeignKey("Caster.Api.Domain.Models.Apply", "RunId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Run");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Directory", b =>
                {
                    b.HasOne("Caster.Api.Domain.Models.Directory", "Parent")
                        .WithMany("Children")
                        .HasForeignKey("ParentId")
                        .OnDelete(DeleteBehavior.Cascade);

                    b.HasOne("Caster.Api.Domain.Models.Project", "Project")
                        .WithMany("Directories")
                        .HasForeignKey("ProjectId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Parent");

                    b.Navigation("Project");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.File", b =>
                {
                    b.HasOne("Caster.Api.Domain.Models.Directory", "Directory")
                        .WithMany("Files")
                        .HasForeignKey("DirectoryId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Caster.Api.Domain.Models.User", "LockedBy")
                        .WithMany()
                        .HasForeignKey("LockedById");

                    b.HasOne("Caster.Api.Domain.Models.User", "ModifiedBy")
                        .WithMany()
                        .HasForeignKey("ModifiedById");

                    b.HasOne("Caster.Api.Domain.Models.Workspace", "Workspace")
                        .WithMany("Files")
                        .HasForeignKey("WorkspaceId")
                        .OnDelete(DeleteBehavior.Cascade);

                    b.Navigation("Directory");

                    b.Navigation("LockedBy");

                    b.Navigation("ModifiedBy");

                    b.Navigation("Workspace");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.FileVersion", b =>
                {
                    b.HasOne("Caster.Api.Domain.Models.File", "File")
                        .WithMany("FileVersions")
                        .HasForeignKey("FileId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Caster.Api.Domain.Models.User", "ModifiedBy")
                        .WithMany()
                        .HasForeignKey("ModifiedById");

                    b.HasOne("Caster.Api.Domain.Models.User", "TaggedBy")
                        .WithMany()
                        .HasForeignKey("TaggedById");

                    b.Navigation("File");

                    b.Navigation("ModifiedBy");

                    b.Navigation("TaggedBy");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Host", b =>
                {
                    b.HasOne("Caster.Api.Domain.Models.Project", "Project")
                        .WithMany()
                        .HasForeignKey("ProjectId");

                    b.Navigation("Project");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.HostMachine", b =>
                {
                    b.HasOne("Caster.Api.Domain.Models.Host", "Host")
                        .WithMany("Machines")
                        .HasForeignKey("HostId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Caster.Api.Domain.Models.Workspace", "Workspace")
                        .WithMany()
                        .HasForeignKey("WorkspaceId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Host");

                    b.Navigation("Workspace");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.ModuleVersion", b =>
                {
                    b.HasOne("Caster.Api.Domain.Models.Module", "Module")
                        .WithMany("Versions")
                        .HasForeignKey("ModuleId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Module");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Partition", b =>
                {
                    b.HasOne("Caster.Api.Domain.Models.Pool", null)
                        .WithMany("Partitions")
                        .HasForeignKey("PoolId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Plan", b =>
                {
                    b.HasOne("Caster.Api.Domain.Models.Run", "Run")
                        .WithOne("Plan")
                        .HasForeignKey("Caster.Api.Domain.Models.Plan", "RunId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Run");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Run", b =>
                {
                    b.HasOne("Caster.Api.Domain.Models.User", "CreatedBy")
                        .WithMany()
                        .HasForeignKey("CreatedById");

                    b.HasOne("Caster.Api.Domain.Models.User", "ModifiedBy")
                        .WithMany()
                        .HasForeignKey("ModifiedById");

                    b.HasOne("Caster.Api.Domain.Models.Workspace", "Workspace")
                        .WithMany("Runs")
                        .HasForeignKey("WorkspaceId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("CreatedBy");

                    b.Navigation("ModifiedBy");

                    b.Navigation("Workspace");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.UserPermission", b =>
                {
                    b.HasOne("Caster.Api.Domain.Models.Permission", "Permission")
                        .WithMany("UserPermissions")
                        .HasForeignKey("PermissionId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Caster.Api.Domain.Models.User", "User")
                        .WithMany("UserPermissions")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Permission");

                    b.Navigation("User");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Vlan", b =>
                {
                    b.HasOne("Caster.Api.Domain.Models.Partition", null)
                        .WithMany("Vlans")
                        .HasForeignKey("PartitionId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Workspace", b =>
                {
                    b.HasOne("Caster.Api.Domain.Models.Directory", "Directory")
                        .WithMany("Workspaces")
                        .HasForeignKey("DirectoryId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Caster.Api.Domain.Models.Host", "Host")
                        .WithMany()
                        .HasForeignKey("HostId");

                    b.Navigation("Directory");

                    b.Navigation("Host");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Directory", b =>
                {
                    b.Navigation("Children");

                    b.Navigation("Files");

                    b.Navigation("Workspaces");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.File", b =>
                {
                    b.Navigation("FileVersions");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Host", b =>
                {
                    b.Navigation("Machines");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Module", b =>
                {
                    b.Navigation("Versions");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Partition", b =>
                {
                    b.Navigation("Vlans");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Permission", b =>
                {
                    b.Navigation("UserPermissions");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Pool", b =>
                {
                    b.Navigation("Partitions");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Project", b =>
                {
                    b.Navigation("Directories");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Run", b =>
                {
                    b.Navigation("Apply");

                    b.Navigation("Plan");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.User", b =>
                {
                    b.Navigation("UserPermissions");
                });

            modelBuilder.Entity("Caster.Api.Domain.Models.Workspace", b =>
                {
                    b.Navigation("Files");

                    b.Navigation("Runs");
                });
#pragma warning restore 612, 618
        }
    }
}
