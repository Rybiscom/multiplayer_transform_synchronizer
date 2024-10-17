# multiplayer_transform_synchronizer

Hey guys 👋
I'm writing my multiplayer game on Godot. 
And since I haven't found a suitable solution for interpolating motion on the client side, I wrote my own script.
This script interpolates the position, rotation and scale of an object.
-
How to use it?
1) Add a new Node3D node to your character instead of the MultiplayerSynchronizer node.
2) Add my script to this node.

Since the script does not synchronize the spawning of the object, you need to add a MultiplayerSpawner node to the main scene and configure it to spawn your character.

Script variables:
TrackThisObject - Add the main node of the player or character whose parameters will be synchronized
SyncPosition, SyncRotation, SyncScale - Which parameters should be synchronized.
InterpolationOffsetMin, InterpolationOffsetMax - Since the script automatically adjusts the delay required for interpolation, these values allow you to set the maximum and minimum delay (you can leave them at default 1-500).

As I have already written the script automatically adjusts the delay on each object depending on the speed of your server and the latency of the Internet connection with the player.
This is necessary for smooth interpolation
Also the data is sent only if it has been changed, if the object does not move - the data is not updated.

The script is not perfect, for example in the current version the script tracks any change (position, rotation, scale) and at any change of even 1 parameter it synchronizes all other parameters too
If you want, you can finalize it yourself
But if you need a quick solution, I think this script will be perfect for you.
