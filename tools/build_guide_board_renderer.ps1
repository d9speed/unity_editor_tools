[CmdletBinding()]
param([string]$output_path)

$ErrorActionPreference = 'Stop'
$repo_root = Split-Path -Parent $PSScriptRoot
$renderer_root = Join-Path $repo_root 'packages\io.github.d9speed.editor_core\Renderer~'
$source_path = Join-Path $renderer_root 'guide_board_renderer.cs'
if ([string]::IsNullOrWhiteSpace($output_path)) {
    $output_path = Join-Path $renderer_root 'guide_board_renderer.dll'
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler is required to build the renderer.' }
$output_path = [IO.Path]::GetFullPath($output_path)
$output_directory = Split-Path -Parent $output_path
New-Item -ItemType Directory -Path $output_directory -Force | Out-Null
& $compiler '/nologo' '/target:library' '/optimize+' '/codepage:65001' "/out:$output_path" '/reference:System.Drawing.dll' $source_path
if ($LASTEXITCODE -ne 0) { throw 'Guide Board renderer compilation failed.' }
Write-Output "Renderer ready: $output_path"
