RUMI VOICE-ONLY LISTENER
========================

There are no activation buttons in this version.

1. Keep the Rumi Arduino sketch uploaded to the ESP32.
2. Connect the ESP32 so it appears as COM5.
3. Close Arduino IDE's Serial Monitor.
4. Open RumiVoiceListener.exe.
5. Leave the window open or minimized.
6. Say "Heyy Rumi" or "Hey Rumi".

The app starts the laptop microphone automatically. When it recognizes the wake
phrase, it sends "wake" to the ESP32 and Rumi changes to her active animation.
It then listens to the following command and returns her to sleep after 20
seconds. If the ESP32 is unplugged, the app waits and reconnects to COM5.

The microphone bar should move when you speak. If it does not, enable microphone
access for desktop applications in Windows Settings > Privacy & security >
Microphone. If Windows reports that speech recognition is unavailable, install
an English speech language under Settings > Time & language > Language & region.

Close this listener before uploading another sketch from Arduino IDE, because
only one program can use COM5 at a time.
