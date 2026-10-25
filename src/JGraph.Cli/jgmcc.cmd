@echo off
rem JGMCC.CMD - JGraph's mcc from a command prompt: jgraph.exe mcc with these arguments, as
rem R2025b's bin\mcc.bat runs its mcc (compiler and SDK plan, stage K7).
"%~dp0jgraph.exe" mcc %*
exit /b %errorlevel%
