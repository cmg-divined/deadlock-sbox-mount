Deadlock mount for s&box
========================

Install: copy this "deadlock" folder into s&box's mount folder, so it reads
    sbox/mount/deadlock/deadlock.dll
    sbox/mount/deadlock/Assets/shaders/...
then start s&box (or the Sandbox game) and enable the Deadlock mount. Deadlock must be installed through Steam.

What's in it: the mount (models, materials, textures, maps, particle systems, animation graphs from Deadlock's
VPKs) and the components its maps and heroes use (Deadlock Map Lighting, Deadlock Particle Effect, Deadlock
Animator, Deadlock Entity...), plus the shaders its materials use.

Don't keep it installed while working on the deadlockmount project in the editor: both register a mount called
"deadlock", the installed one loads first and the project's (with your latest changes) is refused. Remove the
folder from sbox/mount before opening the project.

Rebuild from the project with Build/build-mount.cmd (the editor compiles the shaders it copies).
