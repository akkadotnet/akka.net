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

    #Output folder for creating Akka.NET nuget distributions
    :akka_nuspec => {
        :root => File.join(root_folder, "build", "nuget", "Akka"),
        :lib => File.join(root_folder, "build", "nuget", "Akka", "lib"),
        :net45 => File.join(root_folder, "build", "nuget", "Akka", "lib", "net45"),
    },

    :akka_fsharp_nuspec => {
        :root => File.join(root_folder, "build", "nuget", "Akka.FSharp"),
        :lib => File.join(root_folder, "build", "nuget", "Akka.FSharp", "lib"),
        :net45 => File.join(root_folder, "build", "nuget", "Akka.FSharp", "lib", "net45"),
    },

    :akka_remote_nuspec => {
        :root => File.join(root_folder, "build", "nuget", "Akka.Remote"),
        :lib => File.join(root_folder, "build", "nuget", "Akka.Remote", "lib"),
        :net45 => File.join(root_folder, "build", "nuget", "Akka.Remote", "lib", "net45"),
    },

    :akka_slf4net_nuspec => {
        :root => File.join(root_folder, "build", "nuget", "Akka.slf4net"),
        :lib => File.join(root_folder, "build", "nuget", "Akka.slf4net", "lib"),
        :net45 => File.join(root_folder, "build", "nuget", "Akka.slf4net", "lib", "net45"),
    },

    #specifies the locations of the binary DLLs we want to use in NuGet / NUnit
    :bin => {
        :akka => 'placeholder - specify build environment',
        :akka_fsharp => 'placeholder - specify build environment',
        :akka_remote => 'placeholder - specify build environment',
        :akka_slf4net => 'placeholder - specify build environment',
    }
}

Files = {
    :solution => "Akka.sln",
    :version => "VERSION",
    :assembly_info => "SharedAssemblyInfo.cs",

    :akka => {
        :bin => "#{Projects[:akka][:id]}.dll",
        :pdb => "#{Projects[:akka][:id]}.pdb"
    },

    :akka_fsharp => {
        :bin => "#{Projects[:akka_fsharp][:id]}.dll",
        :pdb => "#{Projects[:akka_fsharp][:id]}.pdb"
    },

    :akka_remote => {
        :bin => "#{Projects[:akka_remote][:id]}.dll",
        :pdb => "#{Projects[:akka_remote][:id]}.pdb",
    },

    :akka_slf4net => {
        :bin => "#{Projects[:akka_slf4net][:id]}.dll",
        :pdb => "#{Projects[:akka_slf4net][:id]}.pdb"
    },
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
