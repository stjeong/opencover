OpenCover.Console.exe -register:user -target:..\..\..\tools\NUnit-2.6.2\bin\nunit-console-x86.exe -targetargs:"OpenCover.Test.dll /noshadow" -excludebyattribute:*.ExcludeFromCoverageAttribute -filter:"+[Open*]* -[Open*]*Boot* -[OpenCover.T*]*" -output:opencovertests.xml
