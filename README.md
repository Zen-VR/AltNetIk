## Alternative Network for Inverse Kinematics

VRChats IK update rate is about 6-20Hz this is quite slow with a lot of smoothing it's fine for most things in VR but not dancing, this mod aims to fix that. Players without the mod will see your normal VRChat networked IK. Avatar parameters like face gestures or OSC stuff like eye/face tracking will update at the same rate as your IK.

TLDR: Remote players look same as local, faster IK and avatar parameter update rates, lower ping/IK delay, a few of VRChats networked IK bugs fixed.

#### Nameplate info:
- Packets Per Second (PPS), the amount of IK updates you receive from a player per second (value will be close to their current FPS).
- Ping (server round-trip time), the amount of delay in milliseconds it takes to send an update to the server and back.

#### Known bugs:
- When remote players calibrate/reset their avatar will become broken for a few seconds.
- SDK2 avatars aren't supported.
- Interpolation can cause single frames with broken player rotation, option to disable interpolation in settings.
- If you find any bugs that aren't listed here make an issue [here](https://github.com/Zen-VR/AltNetIk/issues) or send me a DM.


#### Fixed VRChat bugs:

- Bones flipping.

https://user-images.githubusercontent.com/104001796/164467306-f6da9735-d324-47b0-9a75-10af54b4983e.mp4

- Turning head too fast moving player origin (shaky legs when turning fast).

https://user-images.githubusercontent.com/104001796/164468165-bcc211b1-a9a5-4429-bbce-ff79ba20a231.mp4

- Index controller root motion bug, when using Index controllers animations won't move player position.

https://user-images.githubusercontent.com/104001796/164468566-35b82763-110e-4c23-920e-910ee9f6cf6d.mp4

- Differences in smoothing means you can now see fast movements like foot stomping or avatar pens.

https://user-images.githubusercontent.com/104001796/164469092-99fc29f5-f5bd-4c17-bcde-7f55caac7f9e.mp4

https://user-images.githubusercontent.com/104001796/164469569-9a0938ca-602c-4798-a9a6-1be743ed59ea.mp4

- Distance and player count based IK update rate slow down.


#### Hosting your own server:

* Port forward UDP port `9052`, if you don't know how to port forward you can find the appropriate guide for your router [here](https://portforward.com/router.htm).
* Download and extract latest server release [zip](https://github.com/Zen-VR/AltNetIk/releases/latest).
    * On Windows run `AltNetIkServer.exe`.
    * On Linux first install [.Net Core](https://docs.microsoft.com/dotnet/core/install/linux) from your package manager then run either `./start.sh` or `./AltNetIkServer`.


#### This project wouldn't have been possible without the help of these people
[Zettai](https://github.com/ZettaiVR),
[Requi](https://github.com/RequiDev),
[DDAkebono](https://github.com/ddakebono),
[knah](https://github.com/knah),
[Yato](https://github.com/Kiokuu)
