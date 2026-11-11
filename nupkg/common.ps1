# Paths
$packFolder = (Get-Item -Path "./" -Verbose).FullName
$rootFolder = Join-Path $packFolder "../"

function Write-Info   
{
	param(
        [Parameter(Mandatory = $true)]
        [string]
        $text
    )

	Write-Host $text -ForegroundColor Black -BackgroundColor Green

	try 
	{
	   $host.UI.RawUI.WindowTitle = $text
	}		
	catch 
	{
		#Changing window title is not suppoerted!
	}
}

function Write-Error   
{
	param(
        [Parameter(Mandatory = $true)]
        [string]
        $text
    )

	Write-Host $text -ForegroundColor Red -BackgroundColor Black 
}

function Seperator   
{
	Write-Host ("_" * 100)  -ForegroundColor gray 
}

function Get-Current-Version { 
	$commonPropsFilePath = resolve-path "../Directory.Build.props"
	$commonPropsXmlCurrent = [xml](Get-Content $commonPropsFilePath ) 
	$currentVersion = $commonPropsXmlCurrent.Project.PropertyGroup.Version.Trim()
	return $currentVersion
}

function Get-Current-Branch {
	return git branch --show-current
}	   

function Read-File {
	param(
        [Parameter(Mandatory = $true)]
        [string]
        $filePath
    )
		
	$pathExists = Test-Path -Path $filePath -PathType Leaf
	if ($pathExists)
	{
		return Get-Content $filePath		
	}
	else{
		Write-Error  "$filePath path does not exist!"
	}
}

# List of solutions
$solutions = (
    "./"
)

# List of projects
$projects = (
    # framework
    "src/Cike.AspNetCore.MinimalAPIs",
    "src/Cike.AspNetCore.Swagger",
    "src/Cike.Security",
    "src/Cike.MultiTenancy",
    "src/Cike.Caching",
    "src/Cike.Contracts",
    "src/Cike.Core",
    "src/Cike.Cqrs",
    "src/Cike.Data",
    "src/Cike.Data.Domain",
    "src/Cike.Data.EFCore",
    "src/Cike.Data.EFCore.MySql",
    "src/Cike.Data.EFCore.SqlServer",
    "src/Cike.EventBus",
    "src/Cike.EventBus.Adaptive",
    "src/Cike.EventBus.Local",
    "src/Cike.FluentValidation",
    "src/Cike.UniversalId",
    "src/Cike.Uow",
    "src/Locks/Cike.Locks",
    "src/Locks/Cike.Locks.DistributedRedis"
)
