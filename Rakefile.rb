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
require File.expand_path(File.dirname(__FILE__)) + '/buildscripts/nuspec'

#-----------------------
# Environment variables
#-----------------------
@env_buildconfigname = "Release"

def env_buildversion
    version = bumper_version.to_s.split(/\W/)
    "#{version[0]}.#{version[1]}.#{version[2]}.#{version[3]}"
end

def env_nuget_version
    version = bumper_version.to_s.split(/\W/)
    if(version.count == 5)
        "#{version[0]}.#{version[1]}.#{version[2]}-#{version[4]}"
    else
        "#{version[0]}.#{version[1]}.#{version[2]}"
    end
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
    Folders[:bin][:akka] = File.join(Folders[:src], Projects[:akka][:dir],"bin", @env_buildconfigname)
    Folders[:bin][:akka_fsharp] = File.join(Folders[:src], Projects[:akka_fsharp][:dir],"bin", @env_buildconfigname)
    Folders[:bin][:akka_remote] = File.join(Folders[:src], Projects[:akka_remote][:dir],"bin", @env_buildconfigname)
    Folders[:bin][:akka_slf4net] = File.join(Folders[:src], Projects[:akka_slf4net][:dir],"bin", @env_buildconfigname)
end

desc "Wipes out the build folder so we have a clean slate to work with"
task :clean_output_folders => :set_output_folders do
    puts "Flushing build folder..."
    flush_dir(Folders[:nuget_out])
end

desc "Creates all of the output folders we need for ILMerge / NuGet"
task :create_output_folders => :clean_output_folders do
    create_dir(Folders[:out])
    create_dir(Folders[:nuget_out])

    #NuGet folders - Akka
    create_dir(Folders[:akka_nuspec][:root])
    create_dir(Folders[:akka_nuspec][:lib])
    create_dir(Folders[:akka_nuspec][:net45])

    create_dir(Folders[:akka_symbol_nuspec][:root])
    create_dir(Folders[:akka_symbol_nuspec][:lib])
    create_dir(Folders[:akka_symbol_nuspec][:src])
    create_dir(Folders[:akka_symbol_nuspec][:net45])

    #NuGet folders - Akka.FSharp
    create_dir(Folders[:akka_fsharp_nuspec][:root])
    create_dir(Folders[:akka_fsharp_nuspec][:lib])
    create_dir(Folders[:akka_fsharp_nuspec][:net45])

    create_dir(Folders[:akka_fsharp_symbol_nuspec][:root])
    create_dir(Folders[:akka_fsharp_symbol_nuspec][:lib])
    create_dir(Folders[:akka_fsharp_symbol_nuspec][:src])
    create_dir(Folders[:akka_fsharp_symbol_nuspec][:net45])

    #NuGet folders - Akka.Remote
    create_dir(Folders[:akka_remote_nuspec][:root])
    create_dir(Folders[:akka_remote_nuspec][:lib])
    create_dir(Folders[:akka_remote_nuspec][:net45])

    create_dir(Folders[:akka_remote_symbol_nuspec][:root])
    create_dir(Folders[:akka_remote_symbol_nuspec][:lib])
    create_dir(Folders[:akka_remote_symbol_nuspec][:src])
    create_dir(Folders[:akka_remote_symbol_nuspec][:net45])

    #NuGet folders - Akka.slf4net
    create_dir(Folders[:akka_slf4net_nuspec][:root])
    create_dir(Folders[:akka_slf4net_nuspec][:lib])
    create_dir(Folders[:akka_slf4net_nuspec][:net45])
end

#-----------------------
# NuGet Output
#-----------------------
output :akka_main_nuget_output => [:create_output_folders] do |out|
    out.from Folders[:bin][:akka]
    out.to Folders[:akka_nuspec][:net45]
    out.file Files[:akka][:bin]
end

output :akka_symbol_nuget_output => [:create_output_folders] do |out|
    out.from Folders[:bin][:akka]
    out.to Folders[:akka_symbol_nuspec][:net45]
    out.file Files[:akka][:bin]
    out.file Files[:akka][:pdb]
end

task :akka_symbol_src_nuget_output => [:create_output_folders] do |out|
    src = File.join(Folders[:src], Projects[:akka][:dir])
    dest = Folders[:akka_symbol_nuspec][:src]
    FileUtils.cp_r Dir.glob(src + '/*.cs'), dest
    FileUtils.cp_r File.join(src, "Actor"), dest
    FileUtils.cp_r File.join(src, "Configuration"), dest
    FileUtils.cp_r File.join(src, "Dispatch"), dest
    FileUtils.cp_r File.join(src, "Event"), dest
    FileUtils.cp_r File.join(src, "Routing"), dest
    FileUtils.cp_r File.join(src, "Properties"), dest
    FileUtils.cp_r File.join(src, "Serialization"), dest
    FileUtils.cp_r File.join(src, "Util"), dest
end

task :akka_nuget_output => [:akka_main_nuget_output, :akka_symbol_nuget_output, :akka_symbol_src_nuget_output]

output :akka_main_fsharp_nuget_output => [:create_output_folders] do |out|
    out.from Folders[:bin][:akka_fsharp]
    out.to Folders[:akka_fsharp_nuspec][:net45]
    out.file Files[:akka_fsharp][:bin]
end

output :akka_fsharp_symbol_nuget_output => [:create_output_folders] do |out|
    out.from Folders[:bin][:akka_fsharp]
    out.to Folders[:akka_fsharp_symbol_nuspec][:net45]
    out.file Files[:akka_fsharp][:bin]
    out.file Files[:akka_fsharp][:pdb]
end

task :akka_fsharp_symbol_src_nuget_output => [:create_output_folders] do |out|
    src = File.join(Folders[:src], Projects[:akka_fsharp][:dir])
    dest = Folders[:akka_fsharp_symbol_nuspec][:src]
    FileUtils.cp_r Dir.glob(src + '/*.fs'), dest
end

task :akka_fsharp_nuget_output => [:akka_main_fsharp_nuget_output, :akka_fsharp_symbol_nuget_output, :akka_fsharp_symbol_src_nuget_output]

output :akka_main_remote_nuget_output => [:create_output_folders] do |out|
    out.from Folders[:bin][:akka_remote]
    out.to Folders[:akka_remote_nuspec][:net45]
    out.file Files[:akka_remote][:bin]
end

output :akka_remote_symbol_nuget_output => [:create_output_folders] do |out|
    out.from Folders[:bin][:akka_remote]
    out.to Folders[:akka_remote_symbol_nuspec][:net45]
    out.file Files[:akka_remote][:bin]
    out.file Files[:akka_remote][:pdb]
end

task :akka_remote_symbol_src_nuget_output => [:create_output_folders] do |out|
    src = File.join(Folders[:src], Projects[:akka_remote][:dir])
    dest = Folders[:akka_remote_symbol_nuspec][:src]
    FileUtils.cp_r Dir.glob(src + '/*.cs'), dest
    FileUtils.cp_r File.join(src, "Configuration"), dest
    FileUtils.cp_r File.join(src, "Proto"), dest
    FileUtils.cp_r File.join(src, "Properties"), dest
    FileUtils.cp_r File.join(src, "Serialization"), dest
    FileUtils.cp_r File.join(src, "Transport"), dest
end

task :akka_remote_nuget_output => [:akka_main_remote_nuget_output, :akka_remote_symbol_nuget_output, :akka_remote_symbol_src_nuget_output]

output :akka_slf4net_nuget_output => [:create_output_folders] do |out|
    out.from Folders[:bin][:akka_slf4net]
    out.to Folders[:akka_slf4net_nuspec][:net45]
    out.file Files[:akka_slf4net][:bin]
end


desc "Executes all file/copy tasks"
task :all_output => [:akka_nuget_output, 
                    :akka_fsharp_nuget_output,
                    :akka_remote_nuget_output,
                    :akka_slf4net_nuget_output]

#-----------------------
# NuSpec
#-----------------------
desc "Builds a nuspec file for Pigeon"
nuspec :nuspec_akka => [:all_output] do |nuspec|
    nuspec.id = Projects[:akka][:id]
    nuspec.title = Projects[:akka][:title]
    nuspec.version = env_nuget_version
    nuspec.authors = Projects[:akka][:authors]
    nuspec.owners = Projects[:akka][:company]
    nuspec.description = Projects[:akka][:description]
    nuspec.projectUrl = Projects[:projectUrl]
    nuspec.licenseUrl = Projects[:licenseUrl]
    nuspec.language = Projects[:language]   

    #dependencies
    Projects[:akka][:dependencies].each do |key, array|
        nuspec.dependency array[:package], array[:version]
    end
    
    nuspec.tags = Projects[:akka][:nuget_tags]
    nuspec.output_file = File.join(Folders[:nuget_out], "#{Projects[:akka][:id]}-v#{env_nuget_version}(#{@env_buildconfigname}).nuspec");
end

desc "Builds a nuspec file for Pigeon.FSharp"
nuspec :nuspec_akka_fsharp => [:all_output] do |nuspec|
    nuspec.id = Projects[:akka_fsharp][:id]
    nuspec.title = Projects[:akka_fsharp][:title]
    nuspec.version = env_nuget_version
    nuspec.authors = Projects[:akka_fsharp][:authors]
    nuspec.owners = Projects[:akka_fsharp][:company]
    nuspec.description = Projects[:akka_fsharp][:description]
    nuspec.projectUrl = Projects[:projectUrl]
    nuspec.licenseUrl = Projects[:licenseUrl]
    nuspec.language = Projects[:language]
    nuspec.tags = Projects[:akka_fsharp][:nuget_tags]
    nuspec.output_file = File.join(Folders[:nuget_out], "#{Projects[:akka_fsharp][:id]}-v#{env_nuget_version}(#{@env_buildconfigname}).nuspec");

    nuspec.dependency Projects[:akka][:id], env_nuget_version
    #Framework (GAC) assembly references
    Projects[:akka_fsharp ][:framework_assemblies].each do |key, array|
        nuspec.framework_assembly array[:assemblyName], array[:targetFramework]
    end
end

desc "Builds a nuspec file for Pigeon.Remote"
nuspec :nuspec_akka_remote => [:all_output] do |nuspec|
    nuspec.id = Projects[:akka_remote][:id]
    nuspec.title = Projects[:akka_remote][:title]
    nuspec.version = env_nuget_version
    nuspec.authors = Projects[:akka_remote][:authors]
    nuspec.owners = Projects[:akka_remote][:company]
    nuspec.description = Projects[:akka_remote][:description]
    nuspec.projectUrl = Projects[:projectUrl]
    nuspec.licenseUrl = Projects[:licenseUrl]
    nuspec.language = Projects[:language]
    nuspec.tags = Projects[:akka_remote][:nuget_tags]
    nuspec.output_file = File.join(Folders[:nuget_out], "#{Projects[:akka_remote][:id]}-v#{env_nuget_version}(#{@env_buildconfigname}).nuspec");

    nuspec.dependency Projects[:akka][:id], env_nuget_version

    #dependencies
    Projects[:akka_remote][:dependencies].each do |key, array|
        nuspec.dependency array[:package], array[:version]
    end
end

desc "Builds a nuspec file for Akka.slf4net"
nuspec :nuspec_akka_slf4net => [:all_output] do |nuspec|
    nuspec.id = Projects[:akka_slf4net][:id]
    nuspec.title = Projects[:akka_slf4net][:title]
    nuspec.version = env_nuget_version
    nuspec.authors = Projects[:akka_slf4net][:authors]
    nuspec.owners = Projects[:akka_slf4net][:company]
    nuspec.description = Projects[:akka_slf4net][:description]
    nuspec.projectUrl = Projects[:projectUrl]
    nuspec.licenseUrl = Projects[:licenseUrl]
    nuspec.language = Projects[:language]
    nuspec.tags = Projects[:akka_slf4net][:nuget_tags]
    nuspec.output_file = File.join(Folders[:nuget_out], "#{Projects[:akka_slf4net][:id]}-v#{env_nuget_version}(#{@env_buildconfigname}).nuspec");

    nuspec.dependency Projects[:akka][:id], env_nuget_version
    
    #dependencies
    nuspec.dependency Projects[:akka_slf4net][:dependencies][:slf4net][:package], Projects[:akka_slf4net][:dependencies][:slf4net][:version]
end

#executes all of the individual NuSpec tasks
task :nuspec => [:nuspec_akka,
                :nuspec_akka_fsharp,
                :nuspec_akka_remote,
                :nuspec_akka_slf4net]

#-----------------------
# NuGet Pack
#-----------------------
desc "Packs a build of Akka into a NuGet package"
nugetpack :pack_akka => [:nuspec] do |nuget|
    nuget.command = Commands[:nuget]
    nuget.nuspec = "\"#{File.join(Folders[:nuget_out], "#{Projects[:akka][:id]}-v#{env_nuget_version}(#{@env_buildconfigname}).nuspec")}\""
    nuget.base_folder = "\"#{Folders[:akka_nuspec][:root]}\""
    nuget.output = "\"#{Folders[:nuget_out]}\""
end

desc "Packs a build of Akka into a NuGet Symbol package"
nugetpack :pack_akka_symbol => [:nuspec] do |nuget|
    nuget.command = Commands[:nuget]
    nuget.nuspec = "\"#{File.join(Folders[:nuget_out], "#{Projects[:akka][:id]}-v#{env_nuget_version}(#{@env_buildconfigname}).nuspec")}\""
    nuget.base_folder = "\"#{Folders[:akka_symbol_nuspec][:root]}\""
    nuget.output = "\"#{Folders[:nuget_out]}\""
    nuget.symbols = true
end

desc "Packs a build of Akka's FSharp support into a NuGet package"
nugetpack :pack_akka_fsharp => [:nuspec] do |nuget|
    nuget.command = Commands[:nuget]
    nuget.nuspec = "\"#{File.join(Folders[:nuget_out], "#{Projects[:akka_fsharp][:id]}-v#{env_nuget_version}(#{@env_buildconfigname}).nuspec")}\""
    nuget.base_folder = "\"#{Folders[:akka_fsharp_nuspec][:root]}\""
    nuget.output = "\"#{Folders[:nuget_out]}\""
end

desc "Packs a build of Akka's FSharp support into a NuGet Symbol package"
nugetpack :pack_akka_fsharp_symbol => [:nuspec] do |nuget|
    nuget.command = Commands[:nuget]
    nuget.nuspec = "\"#{File.join(Folders[:nuget_out], "#{Projects[:akka_fsharp][:id]}-v#{env_nuget_version}(#{@env_buildconfigname}).nuspec")}\""
    nuget.base_folder = "\"#{Folders[:akka_fsharp_symbol_nuspec][:root]}\""
    nuget.output = "\"#{Folders[:nuget_out]}\""
    nuget.symbols = true
end

desc "Packs a build of Akka.Remote into a NuGet package"
nugetpack :pack_akka_remote => [:nuspec] do |nuget|
    nuget.command = Commands[:nuget]
    nuget.nuspec = "\"#{File.join(Folders[:nuget_out], "#{Projects[:akka_remote][:id]}-v#{env_nuget_version}(#{@env_buildconfigname}).nuspec")}\""
    nuget.base_folder = "\"#{Folders[:akka_remote_nuspec][:root]}\""
    nuget.output = "\"#{Folders[:nuget_out]}\""
end

desc "Packs a build of Akka.Remote into a NuGet Symbol package"
nugetpack :pack_akka_remote_symbol => [:nuspec] do |nuget|
    nuget.command = Commands[:nuget]
    nuget.nuspec = "\"#{File.join(Folders[:nuget_out], "#{Projects[:akka_remote][:id]}-v#{env_nuget_version}(#{@env_buildconfigname}).nuspec")}\""
    nuget.base_folder = "\"#{Folders[:akka_remote_symbol_nuspec][:root]}\""
    nuget.output = "\"#{Folders[:nuget_out]}\""
    nuget.symbols = true
end

desc "Packs a build of Akka into a NuGet package"
nugetpack :pack_akka_slf4net => [:nuspec] do |nuget|
    nuget.command = Commands[:nuget]
    nuget.nuspec = "\"#{File.join(Folders[:nuget_out], "#{Projects[:akka_slf4net][:id]}-v#{env_nuget_version}(#{@env_buildconfigname}).nuspec")}\""
    nuget.base_folder = "\"#{Folders[:akka_slf4net_nuspec][:root]}\""
    nuget.output = "\"#{Folders[:nuget_out]}\""
end

desc "Packs all of the Pigeon NuGet packages"
task :pack => [:pack_akka, :pack_akka_symbol,
            :pack_akka_fsharp, :pack_akka_fsharp_symbol,
            :pack_akka_remote, :pack_akka_remote_symbol,
            :pack_akka_slf4net]