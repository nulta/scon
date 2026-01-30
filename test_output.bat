@echo off

:loop

timeout /t 1 /nobreak >nul
echo Testing... %time%

goto loop
