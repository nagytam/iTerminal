RD /S /Q packages
MKDIR packages

RD /S /Q TestResults
MKDIR TestResults

for /d /r . %%d in (bin,obj) do @if exist "%%d" rd /s/q "%%d"

rem msbuild /t:clean_folders
