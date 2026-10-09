param([Parameter(Mandatory=$true)][string]$CompilerPath,[Parameter(Mandatory=$true)][string]$NetAssemblies,[string]$BepInExCore,[string]$GameInterop,[switch]$Tests)
$ErrorActionPreference='Stop'
$taskOut=Join-Path $PSScriptRoot 'build';[void](New-Item -ItemType Directory -Path $taskOut -Force)
$taskRefs=@($NetAssemblies);$taskSources=@('ArabicLayout.cs','ArabicForms.cs','MeasuredLineGeometry.cs','NativeLineRanges.cs','NativeSectionAdapter.cs','NativeGeometryTransaction.cs')
if($Tests){$taskTarget='exe';$taskName='NativeTransactionHarness';$taskSources+='NativeTransactionHarness.cs'}else{
 if(!$BepInExCore -or !$GameInterop){throw 'BepInEx core and generated game interop directories are required'}
 $taskRefs+=@($BepInExCore,$GameInterop);$taskTarget='library';$taskName='AcademiaArabic.NativeAdapter';$taskSources+='Plugin.cs'
}
$taskArgs=[Collections.Generic.List[string]]::new();foreach($flag in @('/nostdlib+',('/target:'+$taskTarget),'/langversion:latest','/utf8output')){$taskArgs.Add($flag)}
$taskArgs.Add('/out:"'+(Join-Path $taskOut ($taskName+'.dll'))+'"')
foreach($dir in $taskRefs){foreach($file in Get-ChildItem -LiteralPath $dir -Filter '*.dll'){
 try{[void][Reflection.AssemblyName]::GetAssemblyName($file.FullName);$taskArgs.Add('/reference:"'+$file.FullName+'"')}catch{}
}}
foreach($name in $taskSources){$taskArgs.Add('"'+(Join-Path $PSScriptRoot ('runtime/'+$name))+'"')}
$taskRsp=Join-Path $taskOut ($taskName+'.rsp');[IO.File]::WriteAllLines($taskRsp,$taskArgs,[Text.UTF8Encoding]::new($false))
& $CompilerPath /noconfig ('@'+$taskRsp);if($LASTEXITCODE -ne 0){throw 'Compilation failed'}
if($Tests){[IO.File]::WriteAllText((Join-Path $taskOut ($taskName+'.runtimeconfig.json')),'{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"6.0.7"}}}')}
