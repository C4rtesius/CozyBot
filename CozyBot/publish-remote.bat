set PATH=%PATH%;C:\Program Files\7-Zip\

set mode=%1
set /p remote=<remote.txt

dotnet publish -c %mode%

set filename=publish-%mode%

7z a -ttar %filename%.tar .\bin\%mode%\netcoreapp3.1\publish\* -bb3
7z a -txz %filename%.tar.xz %filename%.tar -bb3

pscp -C -v %filename%.tar.xz %remote%

pause