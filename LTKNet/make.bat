: The project file dependencies don't seem to actually work.
: Here's a "cheat sheet" to make it all actually build

call "C:\Program Files\Microsoft Visual Studio 8\Common7\Tools\vsvars32.bat"

msbuild LLRP\LLRP.csproj /t:Clean
msbuild LLRPVendorExt\LLRPVendorExt.csproj /t:Clean

msbuild LLRPVendorExt\LLRPVendorExt.csproj /t:BeforeBuild
msbuild LLRPVendorExt\LLRPVendorExt.csproj /t:Build
copy LLRPVendorExt\LLRPVendorNameSpace.cs LLRP
copy LLRPVendorExt\VendorExt.cs LLRP

msbuild LLRP\LLRP.csproj /t:BeforeBuild
msbuild LLRP\LLRP.csproj /t:Build

copy LLRP\bin\Debug\LLRP.dll ..\..\..\tm\modules\mercuryapi\cs
