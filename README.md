# MessagePack for CLI

## What is it?

This is MessagePack serialization/deserialization for CLI (Common Language Infrastructure) implementations such as .NET Framework, Silverlight, Mono (including Moonlight.)
This library can be used from ALL CLS compliant languages such as C#, F#, Visual Basic, Iron Pyhton, Iron Ruby, PowerShell, C++/CLI or so.

## Documentation

See [wiki](https://github.com/msgpack/msgpack-cli/wiki)

## Installation

* You can get binary drops from NuGet (currently, pre-release only).
  * There are NO distribution packages other than NuGet, so you have to build your own drops for Mono.

## How to build

### For .NET Framework

1. Install recent Windows SDK (at least, .NET Framework 4 Client Profile and MSBuild is needed.) <br/>
   Or install Visual Studio or Visual Studio Express.
    1. If you want to build unit test assemblies, install NuGet and then restore NUnit packages.
2. Run:

    msbuild MsgPack.csproj

Or open MsgPack.sln in your IDE and run build command in it.

### For Mono

Open MsgPack.mono.sln with MonoDevelop and then click **Build** menu item.
(Of cource, you can build via xbuild.)

## See also

  Wiki                  : https://github.com/yfakariya/msgpack/wiki
  Issue tracker         : https://github.com/msgpack/issues
  MSBuild reference     : http://msdn.microsoft.com/en-us/library/0k6kkbsd.aspx
  Mono xbuild reference : http://www.mono-project.com/Microsoft.Build