$: << './'
require "rubygems"
require "bundler"
Bundler.setup

require 'albacore'
require 'version_bumper'

#-----------------------
# Local dependencies
#-----------------------
require File.expand_path(File.dirname(__FILE__)) + '/buildscripts/projects'
require File.expand_path(File.dirname(__FILE__)) + '/buildscripts/paths'

#-----------------------
# Environment variables
#-----------------------
@env_buildconfigname = "Release"

def env_buildversion
    bumper_version.to_s
end

def env_nuget_version
    version = env_buildversion.split(".")
    "#{version[0]}.#{version[1]}.#{version[2]}.#{version[3]}"
end

#-----------------------
# Control Flow (meant to be called directly)
#-----------------------

desc "Creates a new Release build of Pigeon locally"
task :default => [:build]

desc "Creates a new Debug build of Pigeon locally"
task :debug => [:set_debug_config, :build]

desc "Packs a Release build of Pigeon for NuGet"
task :nuget => [:build, :pack]

desc "Packs a Debug build of Pigeon for NuGet"
task :nuget_debug => [:debug, :pack]

#-----------------------
# Environment variables
#-----------------------
desc "Sets the build environment to Debug"
task :set_debug_config do
    @env_buildconfigname = "Debug"
end

#-----------------------
# MSBuild
#-----------------------

desc "Does a release build of everything in the solution"
msbuild :build => [:assemblyinfo] do |msb|
    msb.properties :configuration => @env_buildconfigname
    msb.targets :Clean, :Build #Does the equivalent of a "Rebuild Solution"
    msb.solution = File.join(Folders[:root], Files[:solution])
end

#-----------------------
# Version Management
#-----------------------

assemblyinfo :assemblyinfo do |asm|
    assemblyInfoPath = File.join(Folders[:src], Files[:assembly_info])

    asm.input_file = assemblyInfoPath
    asm.output_file = assemblyInfoPath

    asm.version = env_buildversion
    asm.file_version = env_buildversion
end

desc "Increments the build number for the project"
task :bump_build_number do
    bumper_version.bump_build
    bumper_version.write(File.join(Folders[:root], Files[:version]))
end

desc "Increments the revision number for the project"
task :bump_revision_number do
    bumper_version.bump_revision
    bumper_version.write(File.join(Folders[:root], Files[:version]))
end

desc "Increments the minor version number for the project"
task :bump_minor_version_number do
    bumper_version.bump_minor
    bumper_version.write(File.join(Folders[:root], Files[:version]))
end

desc "Increments the major version number for the project"
task :bump_major_version_number do
    bumper_version.bump_major
    bumper_version.write(File.join(Folders[:root], Files[:version]))
end

#-----------------------
# Output
#-----------------------
desc "Sets the output / bin folders based on the current build configuration"
task :set_output_folders do
    #.NET 4.5
    Folders[:bin][:pigeon] = File.join(Folders[:src], Projects[:pigeon_net45][:dir],"bin", @env_buildconfigname)
    Folders[:bin][:pigeon_fsharp] = File.join(Folders[:src], Projects[:pigeon_fsharp_net45][:dir],"bin", @env_buildconfigname)
    Folders[:bin][:pigeon_remote] = File.join(Folders[:src], Projects[:pigeon_remote_net45][:dir],"bin", @env_buildconfigname)
end

desc "Wipes out the build folder so we have a clean slate to work with"
task :clean_output_folders => :set_output_folders do
    puts "Flushing build folder..."
    flush_dir(Folders[:nuget_out])
end

desc "Creates all of the output folders we need for ILMerge / NuGet"
task :create_output_folders => :clean_output_folders do
    create_dir(Folders[:out])

    #NuGet folders - Pigeon
    create_dir(Folders[:nuget_out])
    create_dir(Folders[:pigeon_nuspec][:root])
    create_dir(Folders[:pigeon_nuspec][:lib])
    create_dir(Folders[:pigeon_nuspec][:net45])

    #NuGet folders - Pigeon.FSharp
    create_dir(Folders[:nuget_out])
    create_dir(Folders[:pigeon_fsharp_nuspec][:root])
    create_dir(Folders[:pigeon_fsharp_nuspec][:lib])
    create_dir(Folders[:pigeon_fsharp_nuspec][:net45])

    #NuGet folders - Pigeon.Remote
    create_dir(Folders[:nuget_out])
    create_dir(Folders[:pigeon_remote_nuspec][:root])
    create_dir(Folders[:pigeon_remote_nuspec][:lib])
    create_dir(Folders[:pigeon_remote_nuspec][:net45])
end

#-----------------------
# NuGet Output
#-----------------------
output :pigeon_nuget_output => [:create_output_folders] do |out|
    out.from Folders[:bin][:pigeon]
    out.to Folders[:pigeon_nuspec][:net45]
    out.file Files[:pigeon][:bin]
end

output :pigeon_fsharp_nuget_output => [:create_output_folders] do |out|
    out.from Folders[:bin][:pigeon_fsharp]
    out.to Folders[:pigeon_fsharp_nuspec][:net45]
    out.file Files[:pigeon_fsharp][:bin]
end

output :pigeon_remote_nuget_output => [:create_output_folders] do |out|
    out.from Folders[:bin][:pigeon_remote]
    out.to Folders[:pigeon_remote_nuspec][:net45]
    out.file Files[:pigeon_remote][:bin]
    out.file Files[:pigeon_remote][:google_protobuff]
    out.file Files[:pigeon_remote][:google_serialization_protobuff]
end

desc "Executes all file/copy tasks"
task :all_output => [:pigeon_nuget_output, 
                    :pigeon_fsharp_nuget_output,
                    :pigeon_remote_nuget_output]

#-----------------------
# NuSpec
#-----------------------
desc "Builds a nuspec file for Pigeon"
nuspec :nuspec_pigeon => [:all_output] do |nuspec|
    nuspec.id = Projects[:pigeon][:id]
    nuspec.title = Projects[:pigeon][:title]
    nuspec.version = env_nuget_version
    nuspec.authors = Projects[:pigeon][:authors]
    nuspec.owners = Projects[:pigeon][:company]
    nuspec.description = Projects[:pigeon][:description]
    nuspec.projectUrl = Projects[:projectUrl]
    nuspec.licenseUrl = Projects[:licenseUrl]
    nuspec.language = Projects[:language]
    nuspec.tags = Projects[:pigeon][:nuget_tags]
    nuspec.output_file = File.join(Folders[:nuget_out], "#{Projects[:pigeon][:id]}-v#{env_nuget_version}(#{@env_buildconfigname}).nuspec");

    #dependencies
    Projects[:pigeon][:dependencies].each do |key, array|
        nuspec.dependency array[:package], array[:verison]
    end
end

desc "Builds a nuspec file for Pigeon.FSharp"
nuspec :nuspec_pigeon_fsharp => [:all_output] do |nuspec|
    nuspec.id = Projects[:pigeon_fsharp][:id]
    nuspec.title = Projects[:pigeon_fsharp][:title]
    nuspec.version = env_nuget_version
    nuspec.authors = Projects[:pigeon_fsharp][:authors]
    nuspec.owners = Projects[:pigeon_fsharp][:company]
    nuspec.description = Projects[:pigeon_fsharp][:description]
    nuspec.projectUrl = Projects[:projectUrl]
    nuspec.licenseUrl = Projects[:licenseUrl]
    nuspec.language = Projects[:language]
    nuspec.tags = Projects[:pigeon_fsharp][:nuget_tags]
    nuspec.output_file = File.join(Folders[:nuget_out], "#{Projects[:pigeon_fsharp][:id]}-v#{env_nuget_version}(#{@env_buildconfigname}).nuspec");

    #Framework (GAC) assembly references
    Projects[:pigeon_fsharp ][:framework_assemblies].each do |key, array|
        nuspec.framework_assembly array[:assemblyName], array[:targetFramework]
    end
end

desc "Builds a nuspec file for Pigeon.Remote"
nuspec :nuspec_pigeon_remote => [:all_output] do |nuspec|
    nuspec.id = Projects[:pigeon_remote][:id]
    nuspec.title = Projects[:pigeon_remote][:title]
    nuspec.version = env_nuget_version
    nuspec.authors = Projects[:pigeon_remote][:authors]
    nuspec.owners = Projects[:pigeon_remote][:company]
    nuspec.description = Projects[:pigeon_remote][:description]
    nuspec.projectUrl = Projects[:projectUrl]
    nuspec.licenseUrl = Projects[:licenseUrl]
    nuspec.language = Projects[:language]
    nuspec.tags = Projects[:pigeon_remote][:nuget_tags]
    nuspec.output_file = File.join(Folders[:nuget_out], "#{Projects[:pigeon_remote][:id]}-v#{env_nuget_version}(#{@env_buildconfigname}).nuspec");

    #assembly references
    Projects[:pigeon_remote][:references].each do |key, array|
        nuspec.reference array[:file]
    end
end

#executes all of the individual NuSpec tasks
task :nuspec => [:nuspec_pigeon,
                :nuspec_pigeon_fsharp,
                :nuspec_pigeon_remote]

#-----------------------
# NuGet Pack
#-----------------------
desc "Packs a build of Pigeon into a NuGet package"
nugetpack :pack_pigeon => [:nuspec] do |nuget|
    nuget.command = Commands[:nuget]
    nuget.nuspec = File.join(Folders[:nuget_out], "#{Projects[:pigeon][:id]}-v#{env_nuget_version}(#{@env_buildconfigname}).nuspec")
    nuget.base_folder = Folders[:pigeon_nuspec][:root]
    nuget.output = Folders[:nuget_out]
end

desc "Packs a build of Pigeon into a NuGet package"
nugetpack :pack_pigeon_fsharp => [:nuspec] do |nuget|
    nuget.command = Commands[:nuget]
    nuget.nuspec = File.join(Folders[:nuget_out], "#{Projects[:pigeon_fsharp][:id]}-v#{env_nuget_version}(#{@env_buildconfigname}).nuspec")
    nuget.base_folder = Folders[:pigeon_fsharp_nuspec][:root]
    nuget.output = Folders[:nuget_out]
end

desc "Packs a build of Pigeon into a NuGet package"
nugetpack :pack_pigeon_remote => [:nuspec] do |nuget|
    nuget.command = Commands[:nuget]
    nuget.nuspec = File.join(Folders[:nuget_out], "#{Projects[:pigeon_remote][:id]}-v#{env_nuget_version}(#{@env_buildconfigname}).nuspec")
    nuget.base_folder = Folders[:pigeon_remote_nuspec][:root]
    nuget.output = Folders[:nuget_out]
end

desc "Packs all of the Pigeon NuGet packages"
task :pack => [:pack_pigeon,
            :pack_pigeon_fsharp,
            :pack_pigeon_remote]