OpenCover.Console.exe -register:user -target:..\..\..\tools\NUnit-2.6.2\bin\nunit-console-x86.exe -targetargs:"OpenCover.Test.dll /noshadow" -filter:"+[Open*]* -[OpenCover.T*]*" -output:opencovertests.xml -oldstyle
