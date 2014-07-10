open Fake
module FB = Fake.Boot
FB.Prepare {
    FB.Config.Default "src" with
        NuGetDependencies = [{ PackageId = "xunit.runners"; Version = FB.SemanticVersion "1.9.2" }]
}
