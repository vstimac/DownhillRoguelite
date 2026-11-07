@echo off
rem Everyone plays the same mountain (seed 1). Your first life counts: deepest wins.
cd /d "%~dp0"
start "" "DownhillRoguelite.exe" -- --seed=1
