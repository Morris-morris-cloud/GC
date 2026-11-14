try {
	& (Join-Path $PSScriptRoot 'buildps1/build.ps1') @args
	exit 0
}
catch {
	[Console]::Error.WriteLine($_)
	exit 1
}
