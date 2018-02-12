$outputDir = ".\package\"
$build = "Release"
$version = "11.0.0.1"

C:\Episerver\Modules\404handler\src\.nuget\nuget.exe pack ".\src\BVNetwork.404Handler.csproj" -IncludeReferencedProjects -properties Configuration=$build -Version $version -OutputDirectory $outputDir
