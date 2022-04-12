# JADAK

## Device

We will be using the [TingMagic IZAR 4-Port UHF](https://www.atlasrfidstore.com/thingmagic-izar-4-port-uhf-rfid-reader-by-jadak/)

**Common Language** - Mercury API, ThingMagic’s universal programming interface, permits easy software portability across the entire ThingMagic product line – between finished readers and embedded modules.

## SDK

The latest JADAK ThingMagic Mercury API is, v1.35.1.

WARNING:
> Though it states states it support .NET Core, the SDK files such as `LLRP.dll` are written for .NET Framework 3.5.

## Test Software

The Universal Reader Assistant is the application used by Jadak for testing out their various devices. (i.e. the same one Jeff is using).

`Jadak SDK\Software\mercuryapi-1.35.1.103\cs\Samples\UniversalReaderAssistant2.0`

## Upgrade to .NET 5 for Cross-Platform

The MercuryAPI.dll requires the open source project, LLRP.dll, which is built on .NET 2.0.

This document provides the outcome of the feasibility to upgrade the library to .NET 5 and its ability to run on Linux. Overall, I would prefer the 3rd-party vendor to perform this action for supportability and warranty purposes.

## Upgrade Step 1 - .NET Framework 2.0 to 4.0

The upgrade was successful.

## Upgrade Step 2 - .NET Framework 4.0 to .NET 5

The upgrade was performed using Microsoft's tool, `try-convert` and the following outcomes were discovered:

### CodeGenerate.csproj - NuGets

Add NuGet Package References:

* `Microsoft.Build.Framework`
* `Microsoft.Build.Utilities.Core`.

Update `CodeGenerate.csproj` to include `IsImplicitlyDefined="true"` to take care of the following error message:

> _The package reference Microsoft.Build.Framework should not specify a version. Please specify the version in 'xxx\Packages.props' or set VersionOverride to override the centrally defined version._

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.Build.Framework" Version="17.1.0" IsImplicitlyDefined="true" />
    <PackageReference Include="Microsoft.Build.Utilities.Core" Version="17.1.0" IsImplicitlyDefined="true" />
  </ItemGroup>
```

Unfortunately, `<ProjectGroup>` with `<AllowPackageReferenceWithVersion>true</AllowPackageReferenceWithVersion>` doesn't work.

#### LLRP.csproj - NuGets

Add NuGet package, `System.Configuration.ConfigurationManager`

```xml
  <ItemGroup>
    <PackageReference Include="System.Configuration.ConfigurationManager" Version="5.0.0" IsImplicitlyDefined="true" />
  </ItemGroup>
```

#### LLRP.csproj - BeforeBuild

After upgrading to .NET 5, `<Import Project="$(MSBuildBinPath)\Microsoft.CSharp.targets" />`, is removed. This is because it is a .NET Framework 4 library.

MSBUILD BUG:
> Due to the bug 1680, [https://github.com/dotnet/msbuild/issues/1680], the BeforeBuild and AfterBuild targets are longer working in .NET Core projects. MSBuild is overriding these targets with the default SDK target file. You must use, `BeforeTargets="Build"` as a workaround.

### Deprecated Library

1. The LLRP project makes a reference to the deprecated library, `System.Runtime.Remoting`. This library is no longer supported due to its problematic architecture, and is no longer supported. [Ref](https://docs.microsoft.com/en-us/answers/questions/497557/net-50-not-supporting-remotingchannelstcp.html)
2. Suggestion: An upgrade to use, `System.Net` and `System.Net.Sockets` is recommended.
3. `TCPIPClient.cs` relies on the deprecated library and needs upgraded
4. `LLRPClient.xslt` and `.cs` references it but is not used.
5. `LLRPEndPoint.xslt` and `.cs` references it but is not used.

## References

* [LLRP Source Code](https://sourceforge.net/projects/llrp-toolkit/)
* [https://www.jadaktech.com/products/thingmagic-rfid/thingmagic-mercury-api/]
* [https://www.jadaktech.com/resources/rfid-document-library/thingmagic-mercury-api-sdk-1-31-4-35/]
