<#
.NAME 
Build-Solution
.SYNOPSIS
A build tool with opinions. It expects a project to be structure according to a known convention.
.DESCRIPTION
This tool will attempt to:
    Clean - Remove all items from /LOG, /TestOutput and /BuildOutput.
    Version - Update the AssemblyInfo of the project to match the value in the /VERSION file.
    Build - Compile the first solution found in the /src folder.
    Test - Execute tests contained within that solution using a /TestOutput to host test artifacts.
    Logging - Build and Test output will be logged to the /log folder.
    Deploy - Copy artifact to a BuildOutput folder at the root level.
    [Optional] Package - Build a nuget package if a nuspec file is found, package is copied to /Packages.
    [Optional] Publish - Publish the package to nuget.org        
.NOTES
This tool expects the following conventions to be true of your project:
    - Source code is stored in /src
    - /src contains the solution (sln) file to use when building
    - /TestOutput is where any test artifacts will be exported (defined as the output path in the test project)
    - /BuildOutput is where the application and any included resources will be placed.
    - /Packages is where any packages (zip, nuget) will be placed, content will come from /BuildOutput
    - Publishing to nuget.org will use nuget command line application, nuget should be in path and configured to allow publsihing.
.PARAMETER buildType
Default is 'Release' but other values defined in the solution can also be supplied.
.PARAMETER package
After build and test the contents of /BuildOutput will be placed in a nuget package and also in a zip file.
.PARAMETER publish
Attempts to publish the packaged artifact to nuget.org using the nuget command line utility.
.PARAMETER version
Gets the value of the /VERSION file.
Sets the content of the /VERSION file to the provided value if one is supplied.
.EXAMPLE Build-Solution 
The AssemblyInfo will be updated with the value from /VERSION, a release build will be executed, tested and deployed to /BuildOutput
.EXAMPLE Build-Solution Clean
Cleans the /Log, /TestOutput and /BuildOutput folders.  No Build is executed - Clean happens by default prior to all builds.
.EXAMPLE Build-Solution -buildType debug
The AssemblyInfo will be updated with the value from /VERSION, a debug build will be executed, tested and deployed to /BuildOutput
.EXAMPLE Build-Solution -package
The AssemblyInfo will be updated with the value from /VERSION, a debug build will be executed, tested and deployed to /BuildOutput.
The contents of /BuildOutput will be packaged into nuget and zip artifacts to the /Packages folder.
.EXAMPLE Build-Solution -publish
The AssemblyInfo will be updated with the value from /VERSION, a debug build will be executed, tested and deployed to /BuildOutput.
The contents of /BuildOutput will be packaged into nuget and zip artifacts.
The nuget package in /Packages will be published to nuget.org
#>

param(
    [Parameter(Mandatory = $false)]
        [string]$BuildType = "Release",

    [Parameter(Mandatory = $false)]
        [switch]$Clean = $False,
        
    [Parameter(Mandatory = $false)]
        [switch]$Package = $False,

    [Parameter(Mandatory = $false)]
        [switch]$Publish = $False,    
    
    [Parameter(Mandatory = $false)]
        [string]$Version = ""
)

#TODO
$msbuild = "c:\Windows\Microsoft.NET\Framework\v4.0.30319\msbuild.exe"

# PATHS
$basePath = Get-Location
$logPath = "$basePath\logs"
$buildPath = "$basePath\BuildOutput"
$testPath = "$basePath\TestOutput"
$packagePath = "$basePath\Packages"

#VERSION
$buildVersion = ""
$fullBuildVersion = "1.0.0"
if(Test-Path .\VERSION){
    $buildVersion = Get-Content .\VERSION
    $fullBuildVersion = "$buildVersion.0"
}

#SOLUTION
$solutionName = Get-ChildItem ./src -filter *.sln -name | Select-Object -First 1
$solutionPath = "$basePath\src\$solutionName"
$solutionName = $solutionName -replace ".sln", ""

#FUNCTIONS
function runCleanFolders{
    # CLEAN
    write-host "Cleaning" -foregroundcolor:blue
    if(!(Test-Path "$logPath"))
    {
        mkdir "$logPath"
    }
    if(!(Test-Path "$buildPath"))
    {
        mkdir "$buildPath"
    }
    if(!(Test-Path "$testPath"))
    {
        mkdir "$testPath"
    }    
    if(!(Test-Path "$packagePath"))
    {
        mkdir "$packagePath"
    }    

    remove-item $logPath\* -recurse
    remove-item $buildPath\* -recurse
    remove-item $testPath\* -recurse    
    remove-item $packagePath\* -recurse    
    
    write-host "Cleaned" -foregroundcolor:blue
    $lastResult = $true
}

function runPreBuild{
   
    write-host "PreBuild"  -foregroundcolor:blue
    Try{
        # Set assembly VERSION
        Get-Content $basePath\src\$solutionName\Properties\AssemblyInfo.Template.txt  -ErrorAction stop |
        Foreach-Object {$_ -replace "//{VERSION}", "[assembly: AssemblyVersion(""$buildVersion"")]"} | 
        Foreach-Object {$_ -replace "//{FILEVERSION}", "[assembly: AssemblyFileVersion(""$buildVersion"")]"} | 
        Set-Content $basePath\src\$solutionName\Properties\AssemblyInfo.cs         
    }
    Catch{
        Write-host "PREBUILD FAILED!" -foregroundcolor:red
        exit
    }
}

function runBuild{    

    # BUILD
    write-host "Building"  -foregroundcolor:blue        
    Invoke-expression "$msbuild $solutionPath /p:configuration=$buildType /t:Clean /t:Build /verbosity:q /nologo > $logPath\LogBuild.log"

    if($? -eq $False){
        Write-host "BUILD FAILED!"
        exit
    }
    
    $content = (Get-Content -Path "$logPath\LogBuild.log")
    $failedContent = ($content -match "error")
    $failedCount = $failedContent.Count
    if($failedCount -gt 0)
    {    
        Write-host "BUILDING FAILED!" -foregroundcolor:red
        $lastResult = $false
        
        Foreach ($line in $content) 
        {
            write-host $line -foregroundcolor:red
        }
    }

    if($lastResult -eq $False){    
        exit
    } 
}



function runTest{
    # TESTING
    write-host "Testing"  -foregroundcolor:blue

    $trxPath = "$basePath\TestOutput\AllTest.trx"
    $resultFile="/resultsfile:$trxPath"

    $testDLLs = get-childitem -path "$basePath\TestOutput\*.*" -include "*.Tests.dll"
    #write-host "get-childitem -path $basePath\TestOutput\*.* -include *.Tests.dll"
    
    #$arguments = "$testDLLs /Enablecodecoverage"
    $arguments = "$testDLLs "
    #write-host "vstest.console.exe $arguments"
    Invoke-Expression "vstest.console.exe $arguments > $logPath\LogTest.log"

    if(!$LastExitCode -eq 0){
        Write-host "TESTING FAILED0!" -foregroundcolor:red
        $lastResult = $false                
    }

    $content = (Get-Content -Path "$logPath\LogTest.log")

    $failedContent = ($content -match "Failed: 0")
    $failedCount = $failedContent.Count    
    if($failedCount -ne 1)
    {    
        Write-host "TESTING FAILED1!" -foregroundcolor:red
        $lastResult = $false
    }
    Foreach ($line in $failedContent) 
    {
        write-host $line -foregroundcolor:blue
    }

    $failedContent = ($content -match "Not Runnable")
    $failedCount = $failedContent.Count
    if($failedCount -gt 0)
    {    
        Write-host "TESTING FAILED2!" -foregroundcolor:red
        $lastResult = $false
    }
    Foreach ($line in $failedContent) 
    {
        write-host $line -foregroundcolor:red
    }

    if($lastResult -eq $False){    
        exit
    }
}

function runNugetPublish{
    # DEPLOYING
    write-host "Publishing Nuget package" -foregroundcolor:blue
    $outputName = ".\releases\$solutionName.$fullBuildVersion.nupkg"
    nuget push $outputName
}

function runNugetPack{
    # Packing
    write-host "Packing" -foregroundcolor:blue
    nuget pack .\src\$solutionName\$solutionName.csproj -OutputDirectory .\releases > $logPath\LogPacking.log     
    if($? -eq $False){
        Write-host "PACK FAILED!"  -foregroundcolor:red
        exit
    }
}

function runZipOutput{
    # ZIPPING
    write-host "Zipping" -foregroundcolor:blue
    $outputName = $solutionName+"_V"+$buildVersion+"_BUILD.zip"
    zip a -tzip .\releases\$outputName -r .\BuildOutput\*.* > $logPath\LogZipping.log    
    if($? -eq $False){
        Write-host "Zipping FAILED!"  -foregroundcolor:red
        exit
    }
}


if($buildType -eq "debug"){
    $buildType="Debug"
}

if($Clean){    
    runCleanFolders  
    exit
}

runCleanFolders
if($buildVersion -ne ""){
    runPreBuild # Skip if no version file is present
}
runBuild 
runTest    


if($Publish){
    $Package = $true
}
if($Package){
    runZipOutput
    runNugetPack 
}
if($Publish){
    runNugetPublish 
} 

Write-Host Finished -foregroundcolor:blue