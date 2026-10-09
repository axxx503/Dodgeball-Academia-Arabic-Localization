param([Parameter(Mandatory=$true)][string]$CompilerPath,[Parameter(Mandatory=$true)][string]$FrameworkDirectory,[Parameter(Mandatory=$true)][string]$ExistingSetup)
$ErrorActionPreference='Stop'
$taskOut=Join-Path $PSScriptRoot 'build/installer';[void](New-Item -ItemType Directory -Path $taskOut -Force)
$taskAssembly=[Reflection.Assembly]::LoadFile((Resolve-Path -LiteralPath $ExistingSetup).Path)
foreach($taskName in @('manifest','payload')){
 $taskStream=$taskAssembly.GetManifestResourceStream($taskName);if(!$taskStream){throw 'Required installer resource missing'}
 $taskPath=Join-Path $taskOut ($(if($taskName -eq 'manifest'){'manifest.xml'}else{'payload.zip'}))
 $taskFile=[IO.File]::Create($taskPath);try{$taskStream.CopyTo($taskFile)}finally{$taskFile.Dispose();$taskStream.Dispose()}
}
$taskArgs=[Collections.Generic.List[string]]::new();foreach($flag in @('/nostdlib+','/target:winexe','/langversion:latest','/utf8output')){$taskArgs.Add($flag)}
$taskArgs.Add('/out:"'+(Join-Path $taskOut 'Dodgeball-Academia-Arabic-Setup.exe')+'"')
$taskArgs.Add('/win32manifest:"'+(Join-Path $PSScriptRoot 'installer.manifest')+'"')
foreach($name in @('mscorlib','System','System.Core','System.Drawing','System.Windows.Forms','System.Xml','System.Xml.Linq','System.IO.Compression','System.IO.Compression.FileSystem')){$taskArgs.Add('/reference:"'+(Join-Path $FrameworkDirectory ($name+'.dll'))+'"')}
$taskArgs.Add('/resource:"'+(Join-Path $taskOut 'manifest.xml')+'",manifest');$taskArgs.Add('/resource:"'+(Join-Path $taskOut 'payload.zip')+'",payload')
$taskArgs.Add('"'+(Join-Path $PSScriptRoot 'Installer.cs')+'"')
$taskRsp=Join-Path $taskOut 'installer.rsp';[IO.File]::WriteAllLines($taskRsp,$taskArgs,[Text.UTF8Encoding]::new($false))
& $CompilerPath /noconfig ('@'+$taskRsp);if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
