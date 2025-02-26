# Android App

This guide will focus on the JetBrains Rider integration with Android Emulation.

## Emulator Installation

To use Android Emulation in Rider, you need to install the Android SDK and Emulator first.

Hint: Make sure to run Rider as Administrator to avoid permission issues when using the builtin installer.

1. Navigate to the "Device Manager" Tab
2. Select the "plus" icon to add a new device
3. Select the "Medium Phone" preset and click "Next"
4. Now you are promoted to select a system image. Locate the Android 15 (API 35) image for x86_64 and select it
5. In the right tab, you will find the action to download the SDK if you haven't done so already.
6. Then you can click the download arrow next to the image to download this specific image
7. Now you can see the virtual device in the list. You can start, edit and delete it as you wish.

## Connect to Phone

To connect to a physical phone, you need to enable USB Debugging on your phone and connect it to your PC.

Alternatively, you can use the "ADB" tool to connect to your phone over the network.

Hint: Rider also has a nice UI for that, check the top navbar for the device selector!
