# post_build_dev.ps1
# Runs after every Debug build of ViewReferenceSystem.
# 1. Reads version from compiled DLL
# 2. Uploads DLLs to Firebase dev channel
# 3. Bumps patch number in AssemblyInfo.cs for next build
#
# Called from .csproj post-build event:
#   powershell -ExecutionPolicy Bypass -File "$(ProjectDir)post_build_dev.ps1" "$(ConfigurationName)" "$(TargetDir)" "$(ProjectDir)"

param(
    [string]$Configuration,
    [string]$TargetDir,
    [string]$ProjectDir
)

# Only run on Debug builds
if ($Configuration -ne "Debug") {
    Write-Host "  [VRS Deploy] Skipping - not a Debug build ($Configuration)"
    exit 0
}

Write-Host ""
Write-Host "  ======================================"
Write-Host "  VRS Dev Deploy - Firebase Upload"
Write-Host "  ======================================"
Write-Host ""

# Configuration
$FirebaseDb    = "https://view-reference-default-rtdb.firebaseio.com"
$FirebaseStore = "https://firebasestorage.googleapis.com/v0/b/view-reference.firebasestorage.app"
$ApiKey        = "AIzaSyBUrlLM0hyeIqU8KgkPV2jdgJbZuZSaaDM"
$Email         = "revit-plugin@imeg-internal.com"
$Password      = "Welcom32IM@87"
$Channel       = "dev"

$RequiredFiles = @(
    "ViewReferenceSystem.dll",
    "ViewReferenceSystem.dll.config",
    "Newtonsoft.Json.dll"
)
$OptionalFiles = @(
    "DetailReferenceFamily.rfa",
    "Microsoft.CodeAnalysis.dll",
    "Microsoft.CodeAnalysis.CSharp.dll",
    "ViewReferenceSystem.addin"
)

# Read version from compiled DLL
Write-Host "  Reading version from DLL..." -ForegroundColor Gray

$dllPath = Join-Path $TargetDir "ViewReferenceSystem.dll"
try {
    $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dllPath).FileVersion
    $currentVersion = $fileVersion.Trim()
    Write-Host "  Current version: $currentVersion" -ForegroundColor White
} catch {
    $currentVersion = "3.3.0.0"
    Write-Host "  WARNING: Could not read DLL version, using $currentVersion" -ForegroundColor Yellow
}

# Firebase Auth
Write-Host "  Authenticating with Firebase..." -ForegroundColor Gray

try {
    $authBody = @{
        email             = $Email
        password          = $Password
        returnSecureToken = $true
    } | ConvertTo-Json

    $authResponse = Invoke-RestMethod `
        -Uri "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=$ApiKey" `
        -Method POST `
        -Body $authBody `
        -ContentType "application/json"

    $token = $authResponse.idToken
    Write-Host "  OK: Authenticated" -ForegroundColor Green
} catch {
    Write-Host "  ERROR: Firebase auth failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Build succeeded but dev deploy skipped." -ForegroundColor Yellow
    exit 0
}

# Upload files
Write-Host "  Uploading files to Firebase Storage ($Channel)..." -ForegroundColor Gray

$uploadedCount = 0
$failedRequired = @()

foreach ($fileName in ($RequiredFiles + $OptionalFiles)) {
    $localPath = Join-Path $TargetDir $fileName
    $isRequired = $RequiredFiles -contains $fileName

    if (-not (Test-Path $localPath)) {
        if ($isRequired) {
            Write-Host "  MISSING (required): $fileName" -ForegroundColor Red
            $failedRequired += $fileName
        } else {
            Write-Host "  SKIP (optional): $fileName" -ForegroundColor DarkGray
        }
        continue
    }

    try {
        $fileBytes   = [System.IO.File]::ReadAllBytes($localPath)
        $storagePath = "installer/$Channel/$fileName"
        $encodedPath = [Uri]::EscapeDataString($storagePath)
        $uploadUrl   = "$FirebaseStore/o?uploadType=media&name=$encodedPath"

        $uploadResponse = Invoke-WebRequest `
            -Uri $uploadUrl `
            -Method POST `
            -Headers @{ Authorization = "Bearer $token" } `
            -Body $fileBytes `
            -ContentType "application/octet-stream" `
            -UseBasicParsing

        if ($uploadResponse.StatusCode -eq 200) {
            Write-Host "  OK: $fileName" -ForegroundColor Green
            $uploadedCount++
        } else {
            Write-Host "  WARN: $fileName - HTTP $($uploadResponse.StatusCode)" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "  ERROR: $fileName - $($_.Exception.Message)" -ForegroundColor Red
        if ($isRequired) { $failedRequired += $fileName }
    }
}

if ($failedRequired.Count -gt 0) {
    Write-Host ""
    Write-Host "  ERROR: Required files failed: $($failedRequired -join ', ')" -ForegroundColor Red
    Write-Host "  Dev channel NOT updated." -ForegroundColor Red
    exit 0
}

# Update version metadata in Firebase
Write-Host "  Updating version metadata..." -ForegroundColor Gray

try {
    $versionData = @{
        version      = $currentVersion
        releaseNotes = "Dev build - auto-deployed on $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
        updatedAt    = (Get-Date -Format "yyyy-MM-dd")
        publishedBy  = $env:USERNAME
        buildMachine = $env:COMPUTERNAME
    } | ConvertTo-Json

    $dbUrl = "$FirebaseDb/installer/$Channel.json?auth=$token"
    Invoke-RestMethod -Uri $dbUrl -Method PUT -Body $versionData -ContentType "application/json" | Out-Null

    Write-Host "  OK: Version metadata updated" -ForegroundColor Green
} catch {
    Write-Host "  WARN: Could not update version metadata: $($_.Exception.Message)" -ForegroundColor Yellow
}

# Bump patch number in AssemblyInfo.cs for next build
Write-Host "  Bumping version in AssemblyInfo.cs for next build..." -ForegroundColor Gray

$assemblyInfoPath = Join-Path $ProjectDir "Properties\AssemblyInfo.cs"
if (-not (Test-Path $assemblyInfoPath)) {
    # Try root of project dir
    $assemblyInfoPath = Join-Path $ProjectDir "AssemblyInfo.cs"
}

if (Test-Path $assemblyInfoPath) {
    try {
        $parts = $currentVersion.Split('.')
        $major = [int]$parts[0]
        $minor = [int]$parts[1]
        $patch = [int]$parts[2] + 1
        $newVersion = "$major.$minor.$patch.0"

        $content = Get-Content $assemblyInfoPath -Raw

        # Replace AssemblyVersion
        $content = $content -replace '\[assembly: AssemblyVersion\("[\d\.]+"\)\]', "[assembly: AssemblyVersion(""$newVersion"")]"

        # Replace AssemblyFileVersion
        $content = $content -replace '\[assembly: AssemblyFileVersion\("[\d\.]+"\)\]', "[assembly: AssemblyFileVersion(""$newVersion"")]"

        Set-Content $assemblyInfoPath $content -NoNewline
        Write-Host "  OK: AssemblyInfo.cs updated to $newVersion (takes effect next build)" -ForegroundColor Green
    } catch {
        Write-Host "  WARN: Could not update AssemblyInfo.cs: $($_.Exception.Message)" -ForegroundColor Yellow
    }
} else {
    Write-Host "  WARN: AssemblyInfo.cs not found at $assemblyInfoPath" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "  Dev deploy complete - v$currentVersion ($uploadedCount files uploaded)" -ForegroundColor Green
Write-Host ""
