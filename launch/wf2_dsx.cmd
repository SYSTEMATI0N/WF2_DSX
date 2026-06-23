@echo off
REM WF2_DSX co-launcher for Wreckfest 2 (FALLBACK).
REM
REM Recommended setup needs no .cmd at all - point Steam launch options straight at the exe:
REM   "<full path>\WF2_DSX.exe" --play %command%
REM
REM This script is only a fallback if launching the game from the exe does not work for you.
REM It starts the bridge hidden and runs the game; the bridge exits on its own when
REM Wreckfest2 closes (--watch-game). This .cmd window itself stays open while you play.

start "" "%~dp0WF2_DSX.exe" --watch-game --hidden
%*
