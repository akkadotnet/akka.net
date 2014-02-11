#----------------------------------
# Paths and file system functions for Pigeon
#----------------------------------
root_folder = File.expand_path("#{File.dirname(__FILE__)}/..")

Folders = {
    :root => root_folder,
    :src => File.join(root_folder, "src"),
    :out => File.join(root_folder, "build"),
    :tools => File.join(root_folder, "tools"),
    :nuget_bin => File.join(root_folder, ".nuget"),
    :nuget_out => File.join(root_folder, "build", "nuget"),

    #Output folder for creating Pigeon nuget distributions
    :pigeon_nuspec => {
        :root => File.join(root_folder, "build", "nuget", "Pigeon"),
        :lib => File.join(root_folder, "build", "nuget", "Pigeon", "lib"),
        :net45 => File.join(root_folder, "build", "nuget", "Pigeon", "lib", "net45"),
    },

    :pigeon_fsharp_nuspec => {
        :root => File.join(root_folder, "build", "nuget", "Pigeon.FSharp"),
        :lib => File.join(root_folder, "build", "nuget", "Pigeon.FSharp", "lib"),
        :net45 => File.join(root_folder, "build", "nuget", "Pigeon.FSharp", "lib", "net45"),
    },

    :pigeon_remote_nuspec => {
        :root => File.join(root_folder, "build", "nuget", "Pigeon.Remote"),
        :lib => File.join(root_folder, "build", "nuget", "Pigeon.Remote", "lib"),
        :net45 => File.join(root_folder, "build", "nuget", "Pigeon.Remote", "lib", "net45"),
    },

    #specifies the locations of the binary DLLs we want to use in NuGet / NUnit
    :bin => {
        :pigeon => 'placeholder - specify build environment',
        :pigeon_fsharp => 'placeholder - specify build environment',
        :pigeon_remote => 'placeholder - specify build environment',
    }
}

Files = {
    :solution => "Pigeon.sln",
    :version => "VERSION",
    :assembly_info => "SharedAssemblyInfo.cs",

    :pigeon => {
        :bin => "#{Projects[:pigeon][:id]}.dll",
        :pdb => "#{Projects[:pigeon][:id]}.pdb"
    },

    :pigeon_fsharp => {
        :bin => "#{Projects[:pigeon_fsharp][:id]}.dll",
        :pdb => "#{Projects[:pigeon_fsharp][:id]}.pdb"
    },

    :pigeon_remote => {
        :bin => "#{Projects[:pigeon_remote][:id]}.dll",
        :pdb => "#{Projects[:pigeon_remote][:id]}.pdb",
        :google_protobuff => "#{Projects[:pigeon_remote][:references][:google_protobuf][:file]}.dll",
        :google_serialization_protobuff => "#{Projects[:pigeon_remote][:references][:google_protobuf_serialization][:file]}.dll",
    }
}

Commands = {
    :nuget => File.join(Folders[:nuget_bin], "NuGet.exe"),
}

#safe function for creating output directories
def create_dir(dirName)
    if !File.directory?(dirName)
        FileUtils.mkdir(dirName) #creates the /build directory
    end
end

#Deletes a directory from the tree (to keep the build folder clean)
def flush_dir(dirName)
    if File.directory?(dirName)
        FileUtils.remove_dir(dirName, true)
    end
end
