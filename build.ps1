param([string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'This tool requires the Windows .NET Framework C# compiler.' }
$sourceFiles = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | Select-Object -ExpandProperty FullName
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
& $compilerPath /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$OutputDirectory\WindowSwitch.exe" "/win32manifest:$PSScriptRoot\WindowSwitch.exe.manifest" "/win32icon:$PSScriptRoot\WindowSwitch.ico" "/resource:$PSScriptRoot\WindowSwitch.ico,WindowSwitch.AppIcon" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Xml.Linq.dll /reference:Microsoft.CSharp.dll $sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ([System.IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\') -ne $PSScriptRoot.TrimEnd('\')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WindowSwitch.exe.config') -Destination $OutputDirectory -Force
}
