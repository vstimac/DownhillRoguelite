DOWNHILL ROGUELITE - Linux beta (playtest 1)

Untested on real Linux hardware yet: please report anything odd.

Start:  ./DownhillRoguelite.x86_64
Race:   ./race-same-mountain.sh   (same mountain for everyone; your first life counts, deepest wins)
If it says "permission denied":  chmod +x DownhillRoguelite.x86_64 race-same-mountain.sh

Needs: 64-bit Linux (x86_64), Vulkan drivers (mesa-vulkan-drivers, or the NVIDIA driver), glibc 2.28 or newer.
Talking with the companion (Enter): copy the "Models" folder from the Windows build next to the game.
Without it the companion just stays silent. Spoken replies use your system's speech-dispatcher if installed.

Controls: same as the Windows README (Esc in game shows them all).
WASD walk - Shift sprint - C careful steps - Space jump - Mouse look - LMB/RMB lean, both brace
E use - F eat - Q drink - I inventory - J journal - M map - Enter talk - Alt+drag move the UI - Esc pause

Crash log: ~/.local/share/godot/app_userdata/Downhill Roguelite/logs/godot.log
